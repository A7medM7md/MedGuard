# ADR 0007: Real-time Monitoring feed (Kafka → SignalR)

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

The Monitoring page polled every 10s: `GET /batches`, then one
`GET /batches/{id}/detail` per monitored batch (N+1). With 20 batches and 10
open screens, that's 1,260 requests a minute, mostly returning unchanged data.
A critical excursion could also take up to 10s to show up.

Alert events now reach Kafka reliably (ADR 0006). But the page shows **every
reading's temperature**, and alerts only fire on breaches, so pushing alerts
alone would leave safe temperatures stale and keep the polling alive.

## Decision

A `KafkaNotifier` hosted service **inside the API** consumes two topics and
pushes to browsers through a typed SignalR hub, `MonitoringHub` (`/hubs/monitoring`):

| Topic | Processing | Pushed as |
|---|---|---|
| `batch-alerts` | dedupe on `message-id`, then throttle | `AlertRaised(AlertNotification)` |
| `sensor-readings` | conflate to the newest reading per batch, flushed every 1s | `ReadingsUpdated(LiveReading[])` |

- **In the API, not a separate service:** the browser already holds a token for
  the API, and the hub reuses its JWT validation and `ApiScope` policy. A
  separate notifier service would duplicate both and add another origin to CORS.
- **Fan-out, not competing consumers:** every API instance has its own browser
  connections, so each instance must see every message. Each process joins with
  a **unique group id** (`notifier-{machine}-{guid}`). Compare the monitoring
  workers, which share one group so each reading is processed once.
- **No commits, `auto.offset.reset=latest`:** a notification replayed after a
  restart is stale. The page loads its snapshot over HTTP, and pushes carry only
  later changes. The database stays the source of truth.
- **Dedupe (`AlertGate`):** the outbox is at-least-once. `message-id`s are kept for 10 min.
- **Throttle (`AlertGate`):** the first alert per (batch, severity, batch status)
  is pushed; identical ones within 30s are counted and reported as
  `SuppressedCount` on the next push. A change of severity or status is a new
  key, so escalations are never held back. This is the UI-side answer to ADR 0005's alert flood.
- **Conflation for readings:** at most one update per batch per second, whatever
  the device count. That bounds browser traffic for the load-test phase.
- **Raw readings, not recorded ones:** `sensor-readings` is upstream of domain
  evaluation, so the page can show a reading the worker later dead-letters. We
  accept that for a wall display ("what the sensor says, now"), because
  publishing a "reading recorded" event through the outbox would add a SQL row
  per reading. Alerts and status changes do come from committed data.
- **Auth:** WebSockets can't carry an `Authorization` header, so the token comes in
  as `?access_token=`, which is accepted **only** on `/hubs/monitoring`.
  `withCredentials: false` on the client, because the CORS policy is `AllowAnyOrigin`.
- **Push first, poll as fallback (Angular):** the first tick always loads the
  snapshot, and later ticks only fetch while the feed isn't `live`. A reconnect
  triggers a single resync, since pushes sent while disconnected are lost.
- **Off by default:** `Notifier:Enabled=false` in `appsettings.json`, `true` in the
  dev launch profile. When it's disabled the hub isn't mapped, the client's
  `/negotiate` gets a 404, and the page polls as before. That keeps production working (ADR 0001).

## Verification

Server side measured locally on 2026-11-08 (20 devices, 10% excursions, ~4 min,
no browser connected). The browser rows haven't been run yet.

| Scenario | Expected | Result |
|---|---|---|
| Steady state, 3 devices | indicator "Live · push"; temperatures move ~1/s; Network tab shows no polling | |
| Excursion → auto-quarantine | badge flips to Quarantined and alert appears in <1s of the device publish | |
| Sustained breach | 1 push per 30s per (batch, severity, status), with `(+N similar)` | 2381 alert events → **217 pushed, 2164 throttled (91%)**, 0 duplicates |
| Readings conflation | ≤1 push per flush interval | 4073 readings → **195 pushes** (~1/s) |
| Notifier's group | a `notifier-…` group on both topics, separate from `monitoring` | `notifier-AHMED-ELGEBALY-<guid>` consuming `batch-alerts` + `sensor-readings` |
| Kafka stopped | indicator stays live (hub is up) but nothing moves: see Consequences | |
| API restarted | client reconnects automatically, resyncs once, back to live | |
| `environment.monitoringHubUrl = null` | "Polling · every 10s", same behaviour as before this ADR | |

## Consequences

- Monitoring traffic goes from N+1 requests every 10s per screen to one snapshot plus a WebSocket.
- **The fallback still N+1s:** production and the first load still use the old
  calls. A single `GET /batches/monitoring` snapshot endpoint would fix both.
- **"Connected" ≠ "fed":** if Kafka goes down while the API is up, the hub stays
  connected and the page shows "live" with nothing arriving. A heartbeat push
  (e.g. every 10s, carrying consumer health) would let the client detect that
  and fall back to polling. Deferred to the resilience phase.
- **Scaling the hub:** every instance consumes everything (fan-out), so no
  SignalR backplane (Redis) is needed: each instance already has every message.
  The cost is N× Kafka reads, which is negligible here.
- **Single tenant:** everything goes to `Clients.All`. Multi-tenant would use
  SignalR groups per organization.
