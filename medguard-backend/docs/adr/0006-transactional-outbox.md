# ADR 0006: Transactional outbox for alert events

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

When a reading raises an alert, two things have to happen: the alert is stored
in SQL Server, and a `BatchAlertRaised` event is published to `batch-alerts` so
the UI can be told in real time (ADR 0007, next). SQL Server and Kafka can't
share a transaction, so doing both directly leaves a gap whichever order we pick:

| Order | Crash between the two steps | Result |
|---|---|---|
| publish, then commit | DB rolls back | The UI shows an alert the database never stored |
| commit, then publish | event never sent | The alert is stored, but nobody is ever notified |

This is the **dual-write problem**. Retrying in a `catch` doesn't fix it,
because a process crash or kill skips the `catch` entirely.

## Decision

Publish through a **transactional outbox**:

1. `ColdChainMonitoringService.RecordAsync` calls `IOutbox.Enqueue(...)` next to
   `Alerts.AddAsync(...)`. `EfOutbox` only adds an `OutboxMessage` row to the
   same scoped `DbContext`, so the existing single `SaveChanges` commits the
   reading, the alert, the batch status **and the event** atomically.
2. `OutboxDispatcher` (a hosted service in `MedGuard.Monitoring.Worker`) polls
   for pending rows, publishes them, and marks them processed.

Details:

- **Ports and adapters:** `IOutbox` is in Application; `OutboxMessage` and `EfOutbox`
  are in Infrastructure. The domain still doesn't know Kafka exists.
- **Both entry points:** HTTP and Kafka readings share `RecordAsync`, so both emit events.
- **Self-contained event:** `BatchAlertRaised` carries batch number, drug, new
  batch status and device, so subscribers never call back into the API. Enums
  travel as strings.
- **Key = BatchId**, same as `sensor-readings`, so a batch's alerts stay ordered within a partition.
- **Headers:** `message-id` (= outbox row Id), `event-type`, `source=outbox`, `occurred-at-utc`.
- **Claiming rows:** `SELECT TOP (n) ... WITH (UPDLOCK, ROWLOCK, READPAST)` inside
  a transaction that also covers the publish and the `ProcessedAtUtc` UPDATE.
  Every worker instance can run a dispatcher safely: each one skips rows another
  has claimed instead of double-publishing or blocking.
- **Producer:** `acks=all`, idempotent, `message.timeout.ms=10s` (row locks are
  held while waiting). The batch is sent all at once and then awaited.
- **Failures:** failed rows stay pending with `Attempts` and `LastError` updated;
  the dispatcher backs off 1s → 30s. There's no outbox DLQ: a pre-serialized
  payload can't be poison, so failures here are broker availability problems.
- **Cleanup:** published rows are deleted after 24h. Kafka is the record from then on.
- **Polling, not CDC:** an idle dispatcher polls every 500ms. Debezium/CDC would
  remove the polling, but it's another system to run, and 500ms is fine for a human-facing alert.

## Guarantees

- **No lost events, no phantom events:** an event exists if and only if its alert committed.
- **At-least-once, not exactly-once:** a crash after Kafka acks but before the
  UPDATE commits republishes those rows. **Consumers must dedupe on `message-id`.**
- **Ordering:** per batch, within one dispatcher batch. Two dispatchers can
  publish one batch's alerts out of order, and so can a failed row retried after
  a later one succeeded. Each event carries `TriggeredAtUtc` for consumers that care.

## Verification

Local run, 2026-11-08: 20 devices, 10% excursion chance, 1 worker, ~4 min. Rows
without a result haven't been run yet.

| Scenario | Expected | Result |
|---|---|---|
| Excursions with Kafka up | each alert → 1 row → published within ~0.5s, `ProcessedAtUtc` set | 2523 alerts committed; `batch-alerts` holds 741+1161+615 = **2517** events; the other **6** were still pending when the worker was killed. Nothing lost, nothing duplicated |
| Kafka stopped mid-stream | rows accumulate as pending, `Attempts`/`LastError` grow, readings still commit | |
| Kafka restarted | backlog drains; event count on `batch-alerts` = alert rows | |
| Worker killed (`taskkill /F`) mid-publish | after restart, every alert is in the topic at least once; duplicates share `message-id` | |
| Two worker instances | no message published twice in steady state (READPAST) | |

## Consequences

- **Production needs the `AddOutboxMessages` migration before the API is
  redeployed**, because the HTTP path now writes outbox rows.
- **Production has no dispatcher (ADR 0001)**, so its outbox rows stay pending
  and accumulate, one per alert (the same growth rate as `Alerts`). That's
  acceptable until the hosting decision is revisited. A cleanup job or an
  `Outbox:Enabled` flag on the write side are the options if it matters.
- The open issue from ADR 0005 (one alert per breaching reading → floods) now
  reaches Kafka as well. Throttling belongs in the notifier (ADR 0007).
