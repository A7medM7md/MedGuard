# ADR 0012: Containerized, self-hosted deployment (medguard.dpdns.org)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Supersedes:** the hosting part of ADR 0001 (runasp.net + Vercel, HTTP polling only)

## Context

Until now production was split: the API and IdentityServer on runasp.net (IIS),
the SPA on Vercel, and **no** Kafka/MQTT pipeline at all (ADR 0001 kept it local).
The goal now: the whole application, pipeline included, on one Linux server,
public as `https://medguard.dpdns.org`, deployed by CI.

Constraints found on the server:

- It is **shared**: host nginx owns port 80 (other sites), a native SQL Server owns
  1433, other Docker stacks bind 3000/5000/5080/5081.
- Only 2 CPUs: building images there takes minutes and slows the neighbours.
- The office firewall (FortiGate) forwards public **2222 → 22 only**. Public 80/443
  are answered by the FortiGate itself, and it stalls QUIC streams.

The first attempt (2026-11-08) used No-IP (`medguard.ddns.net`) and the host nginx.
It worked inside the LAN but could never be public without firewall changes, and
had no way to get a certificate: Let's Encrypt HTTP-01 needs public port 80, and
No-IP free has no DNS API. It was replaced the next day by a Cloudflare Tunnel, and
No-IP, the host nginx vhost and certbot were removed from the server.

## Decision

### Everything in containers, both web apps on Kestrel

One multi-stage `Dockerfile` (a `--target` per image) and one root
`docker-compose.yml`:

| Container | Image target | Notes |
|---|---|---|
| `api` | `api` | MedGuard.API **and** the Angular build in `wwwroot`, all on Kestrel |
| `identity` | `identity` | Duende on Kestrel, `PathBase=/identity`, keys in a volume |
| `edge` | `edge` | nginx, routing only (`deploy/nginx/edge.conf` baked in) |
| `cloudflared` | upstream image | tunnel connector, `tunnel` profile (production only) |
| `ingestion-bridge`, `monitoring-worker` | same names | unchanged code, config via env vars |
| `migrator` | `migrator` | EF Core migrations **bundle**; runs, exits; API and worker wait for it |
| `sqlserver` | mssql 2022, **Express** | free for production; volume `mssql-data` |
| `kafka` + `kafka-init`, `hivemq` | upstream images | internal network only, Kafka log in a volume |
| `simulator`, `kafkalab` | opt-in profiles | the simulator's fake devices have no batch in prod |

No nginx serves the SPA: the API hosts it (`UseStaticFiles` + `MapFallbackToFile`;
`/api`, `/hubs`, `/health` keep returning real 404s; `index.html` is never cached,
hashed bundles forever).

### Public entry: Cloudflare Tunnel → edge

```
browser --https--> Cloudflare --tunnel (http2, outbound from the server)--> cloudflared
   --> edge:80   /identity/*  -> identity:8080   (prefix kept, PathBase)
                 /*           -> api:8080        (SPA, REST, SignalR WebSockets)
```

- **No inbound port, no certificate to manage**: Cloudflare terminates TLS. The
  firewall is untouched.
- The tunnel is remotely managed. The token is in the server's `.env`. The single
  ingress rule, `medguard.dpdns.org → http://edge:80`, lives in the Cloudflare
  dashboard; creating it also creates the DNS record.
- `--protocol http2`: with the default QUIC the connector timed out behind the
  FortiGate. With http2 it registered 4 connections at once.
- The routing lives in the repo (`edge`), not in Cloudflare, so it is versioned,
  deployed with the app, and identical locally (`http://localhost:8088`).
- `edge` passes Cloudflare's `X-Forwarded-Proto: https` through, redirects visitors
  that came over http, and keeps its own redirects relative (`absolute_redirect off`).
  It resolves upstreams per request through Docker DNS, so a redeployed container's
  new IP is picked up immediately.

One origin means no CORS and first-party cookies, so IdentityServer's cookies are
`SameSite=Lax` (`Cookies:SameSite`, also applied to the check-session cookie).

### Issuer vs. internal discovery

The browser sees the issuer `https://medguard.dpdns.org/identity`. For the API,
fetching that URL would mean a round trip out through Cloudflare and back in. So
the API fetches discovery and keys from `http://identity:8080/identity/...`
(`IdentityServer:MetadataAddress`) and lists the public issuer explicitly
(`IdentityServer:ValidIssuers`). Duende derives the issuer from the request, using
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` and the forwarded proto. The `selfhost`
Angular build derives every URL from `window.location.origin`.

### Keys survive restarts

Duende's signing keys and the ASP.NET Data Protection key ring (which encrypts
them, and the login cookies) both live in the `identity-data` volume
(`DataPath=/app/data`).

### CI/CD

`.github/workflows/deploy.yml` runs when **CI** succeeds on `main` (or manually):

1. `docker/bake-action` builds every image from `docker-compose.yml` in one BuildKit
   session (shared stages build once), GHA layer cache, push to
   `ghcr.io/a7medm7md/medguard-*:<sha12>`.
2. SSH to the server, upload `docker-compose.yml` + `deploy/deploy.sh`, run it:
   GHCR login with the job's short-lived token → `pull` → `up -d` (the migrator runs
   first) → wait until `/health/ready` and the discovery document answer 200 through
   `edge` → log out of GHCR → remove this app's older images.

App secrets (SQL `sa` password, M2M client secret, tunnel token) are **not** in
GitHub. They are in `/opt/medguard/.env` (mode 600). GitHub holds only the SSH
credentials.

## Consequences

- One command reproduces production anywhere: `docker compose up -d --build`.
- runasp.net/Vercel keep working: every new setting defaults to the old behaviour.
- Cloudflare is now in the path: it sees the traffic in clear, and a Cloudflare
  outage is a MedGuard outage. Acceptable for this project.
- The live feed is on in production now (Kafka exists), so ADR 0001's polling-only
  restriction no longer applies there.
- SQL Server's `sa` password is set only when its volume is first created. Changing
  `SQL_SA_PASSWORD` in `.env` later also needs `ALTER LOGIN sa WITH PASSWORD = ...`,
  or the health check and the apps fail.

## Verification

Run on the server (2026-11-08/09). "Through edge" = `127.0.0.1:8088` with the headers
Cloudflare sends (`Host: medguard.dpdns.org`, `X-Forwarded-Proto: https`).

| Check | Result |
|---|---|
| Image build on the server (2 vCPU, all targets, cold cache) | ~14 min, load average up to 30. One-time; CI builds on GitHub runners from then on |
| `docker compose up -d` from empty volumes | migrator `Done.` (database created + 6 migrations), topics created, all long-running containers up |
| Routing through edge | `/batches` 200 (deep link → index.html), `/api/batches` 401, `/api/nope` 404, `/health/ready` 200 (sql + live-feed Healthy), `/identity` → relative 301 `/identity/`, discovery `issuer=https://medguard.dpdns.org/identity` |
| `X-Forwarded-Proto: http` | 301 → `https://medguard.dpdns.org/...` |
| Full Auth Code + PKCE login through edge | login → code → token, `iss=https://medguard.dpdns.org/identity`, refresh token issued, `GET /api/batches` 200, SignalR negotiate 200, **WebSocket upgrade 101** |
| `docker compose restart identity` | same signing key `kid` before and after |
| Pipeline: 1 simulated device for 20s | bridge `received=10 acked=10`; worker consumed them and dead-lettered as `batch … not found` (expected: fake device) |
| `deploy/deploy.sh` (with `SKIP_PULL=1`) | rolled the stack incl. `edge` + `cloudflared`, health wait passed, exit 0 |
| cloudflared | QUIC: `timeout: no recent network activity`; http2: 4 connections registered (mrs07, jed03) |
| Memory at idle | ≈1.6 GB total (SQL 657 MB, Kafka 363 MB, HiveMQ 221 MB, worker 165 MB, API 103 MB) |
| Other sites on the shared host nginx | `admin.khatwaegy.com` still 200 after the MedGuard vhost was added and later removed |
| Public `https://medguard.dpdns.org` | **pending**: the tunnel has no public hostname yet (dashboard step), so DNS doesn't resolve |
| `deploy.yml` on GitHub | not run yet: needs the DEPLOY_* repository secrets and a push to `main` |
