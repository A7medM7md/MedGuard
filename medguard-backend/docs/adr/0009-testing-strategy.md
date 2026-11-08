# ADR 0009: Testing strategy (unit + Testcontainers + CI)

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

Phases 4–6 were verified by hand: outbox event counts, the batch-path correctness
run, the alert throttle. Every one of those checks would have to be redone by
hand after every change. The riskiest code (idempotency, batch transactions,
offsets, the outbox) only behaves correctly against a **real** SQL Server
(filtered unique index, transactions) and a **real** Kafka (headers,
delivery). In-memory fakes would pass while production failed.

## Decision

Two test projects, both run by CI on every push and PR (`.github/workflows/ci.yml`).

**`MedGuard.UnitTests`**: pure logic, no I/O, runs in seconds.

| Area | What's pinned down |
|---|---|
| `Batch.RecordReading` | safe / warning / critical / auto-quarantine off / recalled / already quarantined; **a rejected reading leaves the aggregate untouched** (ADR 0008 regression) |
| `AlertGate` | dedupe on message id, throttle window with suppressed count, escalation never throttled, id forgotten after the window |
| `TelemetryValidator` | device can't publish as another device, sensor-range faults, missing ids, garbage, wrong topic |

**`MedGuard.IntegrationTests`**: Testcontainers, a real `mssql/server:2022` and a
real `apache/kafka:3.9.0` (the same images as docker-compose), shared by all
tests in one collection fixture. The real EF migrations are applied, because the
filtered index and the outbox table are part of what's under test. Tests
isolate themselves with fresh batches instead of wiping tables.

| Test | Proves |
|---|---|
| Duplicates inside one batch and across batches | recorded exactly once |
| Domain rejection mid-batch | only that reading fails; nothing half-applied leaks into the transaction |
| Unknown batch | reported, the rest commits |
| In-order apply + outbox | reading 3 sees the quarantine reading 2 caused; one outbox row per alert, same transaction |
| 4 concurrent ingests of one message | exactly 1 row; the losers see Duplicate or the unique-index violation |
| Outbox → Kafka | published with `message-id` = outbox id and `event-type`, row marked processed |
| Batch handler → DLQ | bad JSON and unknown batch dead-lettered **with a reason header**; duplicate skipped |
| Trace propagation (ADR 0011) | one trace id from a Kafka header → batch → SQL outbox row → dispatcher → Kafka header |

## Verified

- 19 unit + 8 integration tests pass locally (Windows, Docker Desktop).
- **The regression test was checked against the bug:** with the old statement
  order put back in `RecordReading`, exactly that one test fails.

## Consequences

- CI needs Docker. GitHub's Ubuntu runners have it; pulling the ~2 GB SQL image
  is most of the integration step's time.
- **Ryuk** (Testcontainers' cleanup container) is pulled from Docker Hub. On a
  flaky network that pull fails with a TLS timeout and every test errors before
  starting. Locally, `TESTCONTAINERS_RYUK_DISABLED=true` works around it; the
  fixture disposes its own containers. CI keeps Ryuk on.
- The workflow YAML was checked by hand; its first run on GitHub is its real validation.
- Not covered yet: the MQTT edge (bridge ↔ HiveMQ) and the SignalR hub. Both need
  a broker or a browser client, and they're candidates for the next round.
