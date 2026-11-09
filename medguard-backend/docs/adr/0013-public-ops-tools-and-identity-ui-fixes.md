# ADR 0013: Ops tools on public sub-domains; IdentityServer UI fixes

- **Status:** Accepted
- **Date:** 2026-11-09
- **Builds on:** ADR 0011 (observability), ADR 0012 (containerized deployment)

## Context

Production (ADR 0012) exposed only the app. Kafka UI, Grafana, Prometheus and Seq
existed in the dev compose file only, and HiveMQ CE has no web UI. Goal: see every
part of the running system from the internet, under `*.medguard.dpdns.org`.

None of these tools is safe to expose as configured for dev: Grafana ran as an
anonymous admin, Seq had no authentication, Kafka UI and Prometheus have no login at all.

Separately, the IdentityServer pages looked broken in production.

## Decision

### One router, one login

Every public hostname of the Cloudflare Tunnel points to the same `edge:80`. The edge
picks the upstream by the first label of the host (`server_name ~^grafana\.` …), so
the domain is never hard-coded. `grafana.localhost:8088` works the same locally.

| Sub-domain | Upstream | Notes |
|---|---|---|
| `grafana.` | grafana:3000 | auth proxy: the basic-auth user is signed in as Admin, no 2nd login, no login form |
| `kafka.` | kafka-ui:8080 | kafbat Kafka UI, **read-only** (`KAFKA_UI_READONLY`) |
| `hivemq.` (`mqtt.`) | `/` mqtt-web:80, `/mqtt` hivemq:8000 | MQTTX Web + the broker's MQTT-over-WebSocket listener |
| `seq.` (`logs.`) | seq:80 | logs + traces; apps now export (`OBSERVABILITY_ENABLED=true`) |
| `prometheus.` | prometheus:9090 | metrics pushed over OTLP |

All of them sit behind **HTTP basic auth at the edge** (`TOOLS_USER`/`TOOLS_PASSWORD`).
The htpasswd file is written at container start (`/docker-entrypoint.d`), so no
credential is baked into an image, and with no password set nobody gets in. The
edge sets `X-WEBAUTH-USER` itself (Grafana trusts it) and strips `Authorization`
before proxying. Containers run in the `ops` compose profile. Prometheus and
Grafana get their repo config baked into small images (`--target prometheus|grafana`),
because the server only has `docker-compose.yml`. Grafana is stateless: dashboards
and the datasource are provisioned.

Swagger UI is also on, at `/swagger` (`Swagger:Enabled`). Its "Authorize" button uses
the SPA client with PKCE, so `/swagger/oauth2-redirect.html` was added to the
client's redirect URIs for each configured origin.

### What was wrong with the IdentityServer pages

1. **Stale CSS from Cloudflare.** Static files had fixed URLs, and Cloudflare cached
   them (`cf-cache-status: HIT`, 4 h). After a deploy, new markup was rendered with
   the old stylesheet. Fix: `asp-append-version="true"` on every local CSS/JS, so the
   URL carries a content hash and changes with the file.
2. **The CSP blocked what the pages need.** The stock `default-src 'self'` blocked
   the Inter font (Google Fonts), the inline `style=""` attributes, and the
   Cloudflare-injected beacon. Fix: allow `fonts.googleapis.com`/`fonts.gstatic.com`
   and Cloudflare Insights explicitly. Move the inline styles to classes; there is
   still no `'unsafe-inline'`.
3. **Unthemed pages.** Home was Duende's stock welcome page; Diagnostics, Grants and
   Error used bare Bootstrap with non-wrapping claim values. Fix: a MedGuard home
   (link cards, "Open the dashboard" via `App:Url`), styling for the generic pages,
   wrapping long values, stacking columns on small screens.

## Consequences

- Everything is visible from anywhere with one password. Anyone who has it is
  effectively admin of Grafana and can read every log and topic. Rotate it in
  `/opt/medguard/.env` and run `docker compose up -d edge grafana`.
- Stronger option, not done here: Cloudflare Access (Zero Trust) policies on the
  tool hostnames for per-person login with an email code. They work alongside the
  basic auth.
- About +0.7 GB RAM for the ops profile (Kafka UI 268 MB, Grafana 141 MB, Seq 116 MB,
  Prometheus 87 MB, MQTTX Web 62 MB).

## Verification (server, 2026-11-09)

| Check | Result |
|---|---|
| Each tool host via edge: no credentials / wrong password / right password | 401 / 401 / 200 for all five |
| Grafana via auth proxy | `/api/user` → `admin`, `isGrafanaAdmin: true`; dashboard "MedGuard pipeline" provisioned |
| Kafka UI | topics `batch-alerts`, `sensor-readings`, `sensor-readings.dlq` |
| MQTT over WebSocket (`/mqtt`, subprotocol `mqtt`) | 101 Switching Protocols |
| Prometheus jobs | `medguard-api`, `medguard-worker` reporting |
| Seq | events from `medguard-worker` |
| `/swagger/index.html`, `/swagger/v1/swagger.json` | 200, 200 |
| Public login flow (curl through Cloudflare) | token `iss=https://medguard.dpdns.org/identity`, API 200, SignalR WebSocket 101 (HTTP/1.1) |
| Identity pages in Edge (public URL) | themed home with link cards, themed error page, login with the Inter font |
| `medguard-theme.css` before/after | before: Cloudflare HIT on the old file (no `mg-link-grid`); after: `?v=<sha256>` URL |
