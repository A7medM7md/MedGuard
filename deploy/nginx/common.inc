# Shared by every server block of the edge (included at server level).

# Visitors who arrived over plain http at Cloudflare are sent to https.
if ($http_x_forwarded_proto = "http") {
    return 301 https://$host$request_uri;
}

# nginx's own redirects stay relative: an absolute one would use this server's
# scheme (http), not the visitor's (https).
absolute_redirect off;

client_max_body_size 20m;

# IdentityServer's cookies and redirect headers outgrow nginx's 4k/8k default
# ("upstream sent too big header" -> 502 right after login).
proxy_buffer_size 128k;
proxy_buffers 4 256k;
proxy_busy_buffers_size 256k;

# Headers live at server level only: a proxy_set_header inside a location
# would silently drop every one inherited from this level.
proxy_http_version 1.1;
proxy_set_header Host $http_host;
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $edge_forwarded_proto;
proxy_set_header X-Forwarded-Host $http_host;
proxy_set_header Upgrade $http_upgrade;
proxy_set_header Connection $edge_connection;
