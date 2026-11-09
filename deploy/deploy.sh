#!/usr/bin/env bash
# Rolls the MedGuard stack on the server to one image tag. Runs ON the server:
# CI (.github/workflows/deploy.yml) uploads it with docker-compose.yml and calls
#
#   GHCR_LOGIN=1 bash /opt/medguard/deploy.sh <tag>  <<< "$GITHUB_TOKEN"
#
# By hand (images already public, or already pulled):  bash deploy.sh <tag>
# Images built on the server itself:                    SKIP_PULL=1 bash deploy.sh latest
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/medguard}"
TAG="${1:?usage: deploy.sh <image-tag>}"
cd "$APP_DIR"

[ -f .env ] || { echo "ERROR: $APP_DIR/.env is missing (copy .env.example and set the secrets)." >&2; exit 1; }

env_value() { grep -E "^$1=" .env | tail -n1 | cut -d= -f2- || true; }
REGISTRY="$(env_value REGISTRY)"; REGISTRY="${REGISTRY:-ghcr.io/a7medm7md}"
EDGE_PORT="$(env_value EDGE_PORT)"; EDGE_PORT="${EDGE_PORT:-8088}"

# Persist the tag, so a later manual `docker compose up -d` (or a reboot) runs
# the same images.
if grep -q '^IMAGE_TAG=' .env; then
    sed -i "s|^IMAGE_TAG=.*|IMAGE_TAG=${TAG}|" .env
else
    echo "IMAGE_TAG=${TAG}" >> .env
fi

# GHCR packages are private by default: log in with the workflow's short-lived
# token (read from stdin), and log out again whatever happens.
if [ "${GHCR_LOGIN:-0}" = "1" ]; then
    read -r GHCR_TOKEN
    echo "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-github-actions}" --password-stdin >/dev/null
    trap 'docker logout ghcr.io >/dev/null 2>&1 || true' EXIT
fi

# SKIP_PULL=1: images were built on this machine (docker compose build).
if [ "${SKIP_PULL:-0}" != "1" ]; then
    echo "==> Pulling ${REGISTRY}/medguard-*:${TAG}"
    docker compose pull --quiet
fi

echo "==> Starting the stack"
# The migrator runs first (depends_on: service_completed_successfully).
docker compose up -d --remove-orphans --no-build

echo "==> Waiting for the API and IdentityServer to report healthy (through edge)"
ok=0
for _ in $(seq 1 60); do
    api=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${EDGE_PORT}/health/ready" || true)
    ids=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${EDGE_PORT}/identity/.well-known/openid-configuration" || true)
    if [ "$api" = "200" ] && [ "$ids" = "200" ]; then ok=1; break; fi
    sleep 5
done

docker compose ps
if [ "$ok" != "1" ]; then
    echo "ERROR: not healthy after 5 minutes (api=${api:-?}, identity=${ids:-?})." >&2
    docker compose logs --tail 80 migrator api identity edge monitoring-worker >&2 || true
    exit 1
fi

# Remove this app's images from older deploys (never touches other projects' images).
docker images --format '{{.Repository}}:{{.Tag}}' \
    | grep -E "^${REGISTRY}/medguard-[a-z-]+:" \
    | grep -v ":${TAG}\$" \
    | xargs -r docker rmi >/dev/null 2>&1 || true

echo "==> Deployed ${TAG}"
