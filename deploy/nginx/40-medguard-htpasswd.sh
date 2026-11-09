#!/bin/sh
# Runs from the nginx image's /docker-entrypoint.d before nginx starts.
# Writes the basic-auth file for the ops tools (tools-auth.inc) from env vars,
# so no credential is ever baked into the image.
set -eu
FILE=/etc/nginx/medguard/htpasswd
if [ -n "${TOOLS_PASSWORD:-}" ]; then
    printf '%s:%s\n' "${TOOLS_USER:-admin}" "$(openssl passwd -apr1 "$TOOLS_PASSWORD")" > "$FILE"
    echo "medguard: basic auth enabled for the ops tools (user ${TOOLS_USER:-admin})"
else
    : > "$FILE"
    echo "medguard: TOOLS_PASSWORD not set, the ops tools refuse every login"
fi
chmod 644 "$FILE"
