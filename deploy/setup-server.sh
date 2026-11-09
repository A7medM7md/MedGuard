#!/usr/bin/env bash
# One-time server preparation (Ubuntu with Docker). Run from a checkout of this repo:
#
#   sudo CLOUDFLARE_TUNNEL_TOKEN='<token>' bash deploy/setup-server.sh <deploy-user>
#
#  1. /opt/medguard owned by the deploy user (CI uploads docker-compose.yml there)
#  2. .env with freshly generated strong secrets + the tunnel token, if there
#     isn't one yet (an existing .env is never overwritten)
#
# Nothing is installed on the host and no port is opened: the public entry is
# the Cloudflare Tunnel (cloudflared container, outbound only).
set -euo pipefail

DEPLOY_USER="${1:?usage: sudo CLOUDFLARE_TUNNEL_TOKEN=... bash deploy/setup-server.sh <deploy-user>}"
APP_DIR=/opt/medguard
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"

install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 750 "$APP_DIR"
install -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 640 "$REPO_DIR/docker-compose.yml" "$APP_DIR/docker-compose.yml"
install -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 750 "$REPO_DIR/deploy/deploy.sh" "$APP_DIR/deploy.sh"

if [ ! -f "$APP_DIR/.env" ]; then
    # Not `tr < /dev/urandom | head`: head closing the pipe kills tr with SIGPIPE,
    # which pipefail turns into a silent script exit.
    gen() { local s; s=$(head -c 1024 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9'); echo "${s:0:$1}"; }
    # Guarantee SQL Server's complexity rules: upper + lower + digit + symbol.
    SQL_PWD="Mg-$(gen 24)-9a!"
    sed -e "s|^SQL_SA_PASSWORD=.*|SQL_SA_PASSWORD=${SQL_PWD}|" \
        -e "s|^M2M_CLIENT_SECRET=.*|M2M_CLIENT_SECRET=$(gen 40)|" \
        -e "s|^COMPOSE_PROFILES=.*|COMPOSE_PROFILES=tunnel|" \
        -e "s|^CLOUDFLARE_TUNNEL_TOKEN=.*|CLOUDFLARE_TUNNEL_TOKEN=${CLOUDFLARE_TUNNEL_TOKEN:-}|" \
        "$REPO_DIR/.env.example" > "$APP_DIR/.env"
    chown "$DEPLOY_USER:$DEPLOY_USER" "$APP_DIR/.env"
    chmod 600 "$APP_DIR/.env"
    echo "Created $APP_DIR/.env with generated secrets."
    [ -n "${CLOUDFLARE_TUNNEL_TOKEN:-}" ] || echo "WARNING: no CLOUDFLARE_TUNNEL_TOKEN given; set it in $APP_DIR/.env."
fi

cat <<'EOF'

Done. Next:
  - Cloudflare dashboard: the tunnel's public hostname medguard.dpdns.org -> HTTP  edge:80
  - first deploy: push to main (CI), or:  cd /opt/medguard && docker compose up -d
EOF
