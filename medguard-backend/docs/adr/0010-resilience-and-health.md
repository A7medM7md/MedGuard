# ADR 0010: Resilience and health checks

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

The pipeline already survives failures by design: at-least-once delivery, Kafka
replay, offsets stored after commit, the outbox. What was missing:

- **Visibility.** Nothing could answer "is this instance OK?" without reading its logs.
- **Back-pressure on a dead dependency.** The outbox dispatcher retried Kafka with
  hand-rolled backoff, taking SQL row locks on every attempt.
- **A connected-but-dead live feed** (ADR 0007): the browser said "Live" while
  nothing arrived.

## Decision

### Health endpoints in every service

`/health/live` and `/health/ready` (anonymous, JSON), via `MedGuard.ServiceDefaults`.
The worker and bridge moved from the Worker SDK to the Web SDK just to serve them.
Ports: API as configured, worker `5100+InstanceId`, bridge `5200+InstanceId`.

| Service | Live (restart me) | Ready (don't send me work) |
|---|---|---|
| API | process up | SQL answers; live feed Degraded (not Unhealthy) if Kafka is down, since the page falls back to polling |
| Worker | consume loop ticked within 60s | loop + SQL + outbox (circuit open or a row pending >60s → Degraded) |
| Bridge | process up | MQTT connected; messages waiting on Kafka with no ack for 30s → Degraded |

The worker's liveness comes **from inside the consume loop**: the loop keeps calling
`Consume()` even while retrying, so silence means stuck, and a restart is the fix.
"Retrying for >30s" is Degraded rather than dead: the DB is down, and a restart wouldn't help.

**Every check gets a 5s timeout by default.** Measured: with bad credentials,
`AddDbContextCheck`'s default `CanConnect` took **60s**. On SQL Server, EF retries a
failed login for a minute, waiting for databases that are still being created.
The SQL check now runs `SELECT 1`. Result: an unreachable database gets a 503 in
0.74s; a healthy instance gets a 200 in 0.17s.

### Polly in the outbox dispatcher

Retry (outer, exponential + **jitter**, 1s → 30s, forever) around a **circuit
breaker** (inner: 3 failed publishes in 30s → open 15s → half-open trial). While
the circuit is open, nothing is claimed and no SQL locks are held. Jitter keeps
several workers from retrying in lockstep. The circuit state feeds the readiness check.
Observed with an unreachable database: retries at 1.3s, 0.4s, 1.9s … capped at 30s.

### EF retry: API yes, worker no

`AddInfrastructure(..., enableRetryOnFailure: true)` for the API only. A request
that hits a brief failover is retried instead of returning 500. The worker opts
out on purpose: it already classifies transient errors and re-reads from Kafka
(ADR 0005), and the dispatcher's explicit transaction is incompatible with EF's
retrying execution strategy.

### Live-feed heartbeat (closes ADR 0007's "connected ≠ fed")

The notifier sends `FeedHeartbeat` every 5s **from its consume loop**, with Kafka
health. The browser has a new state, `stale`: connected, but no heartbeat for 15s
(or Kafka reported down). In that state it **polls**. When heartbeats return it
resyncs once and goes back to `live`.

## Consequences

- Probes exist; nothing calls them yet (no orchestrator locally). They're ready for a
  container platform's liveness/readiness settings.
- The worker and bridge now open an HTTP port. Running several instances means
  distinct InstanceIds, which they need anyway.
- Hand-rolled retry remains in the worker's consume loop (seek + backoff, ADR 0005):
  that one is tied to Kafka offsets, and Polly would add nothing there.
