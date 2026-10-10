# ADR 0005: Monitoring consumer (Kafka → SQL)

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

Readings now arrive in `sensor-readings` (ADR 0004). They have to go through
the same `Batch.RecordReading` rules as the HTTP endpoint and be persisted.
Delivery is at-least-once, the database can be unavailable, and some messages
can never be processed.

## Decision

`MedGuard.Monitoring.Worker` is a worker service that reuses the existing
Application and Infrastructure layers. It adds a new entry point,
`IColdChainMonitoringService.IngestAsync`, which shares one private core with
the HTTP path (`RecordReadingAsync`), so both paths obey identical rules.

- **Offsets (ADR 0003):** `StoreOffset` is called only after the message is
  finished (committed to SQL, skipped as a duplicate, or parked in the DLQ).
  Auto-commit then commits only stored offsets. Consumers use CooperativeSticky
  and always `Close()` on shutdown.
- **Idempotency:** `SensorReading.MessageId` is nullable (HTTP readings have no
  id) and has a filtered unique index. `IngestAsync` checks it first (fast path),
  and a unique-index violation (SQL 2601/2627) is treated as a duplicate (race
  backstop). The reading, its alert, and the batch status change are saved in
  one `SaveChanges`, which is one transaction, so a redelivery can't produce a
  second alert.
- **Device time:** `RecordedAtUtc` comes from the device, not the server.
- **Failure classification:**

  | Bucket | Examples | Action |
  |---|---|---|
  | Duplicate | `MessageId` already recorded | skip, store offset |
  | Poison | undeserializable payload, unknown batch, `DomainException` | DLQ with reason, store offset |
  | Transient | any `SqlException`/`TimeoutException` **anywhere in the inner-exception chain** | `Seek` back, exponential backoff (≤30s), retry forever |
  | Unknown | everything else | retry 3×, then DLQ |

  The retry does `Seek` and keeps calling `Consume()` instead of sleeping inside
  the handler, so the consumer never exceeds `max.poll.interval.ms` and isn't
  evicted from the group.
- **One DI scope (one DbContext) per message.**
- **The HTTP endpoint keeps writing directly.** Production has no Kafka (ADR 0001).

## Verification (local, 2026-10-06)

| Scenario | Result |
|---|---|
| 2270 messages for nonexistent batches | all dead-lettered with reason, no stall |
| 3 devices on real batches, 15% excursions | 215 published = 171 recorded + 44 rejected by domain (DLQ); 12 warnings, 93 criticals, 2 batches auto-quarantined |
| Replay of 241 already-recorded readings (offset reset) | `duplicates=241`, `recorded=0`, DB row counts unchanged |
| `MedGuardDb` set OFFLINE for ~29s mid-stream | 196 published = 196 recorded, 0 dead-lettered; backlog drained ~10s after DB returned |
| Steady state, 2 devices × 2/s, no backlog | device → DB latency avg 133ms, p95 175ms, max 191ms |

Two findings along the way:

- **EF wraps `SqlException` in `InvalidOperationException`** ("likely due to a
  transient failure"). Checking only the top-level exception type would have
  sent healthy readings to the DLQ during a DB outage.
- **Head-of-line blocking:** with a backlog of ~750 DLQ-bound messages ahead of
  them in the same partitions, live readings waited up to 40s (avg 23.7s). The
  DLQ produce costs ~46ms per message because each one is awaited. A slow
  failure path delays healthy messages behind it.

## Consequences / open issues

- **Production DB needs the `AddSensorReadingMessageId` migration before the
  API is redeployed.** The EF model now includes the column.
- Processing is sequential per instance. Throughput is measured in the load-test phase.
- **Open domain question:** a critical reading for a `Delivered` batch makes
  `Batch.Quarantine()` throw, so the whole reading is rejected and never stored.
- **Open domain question:** every breaching reading raises a new alert, even on
  an already-quarantined batch (93 criticals in ~40s). The notification phase
  will need deduplication or throttling.
