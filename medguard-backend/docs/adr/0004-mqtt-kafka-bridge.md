# ADR 0004: MQTT → Kafka ingestion bridge

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

Device telemetry arrives on MQTT (ADR 0002) and has to land in the Kafka topic
`sensor-readings` (ADR 0003) without loss. A message can be lost at three points:
the bridge crashes, Kafka is unavailable, or the payload can never be processed.

## Decision

`MedGuard.IngestionBridge` is a stateless worker service.

1. **Ack after Kafka confirms.** `AutoAcknowledge = false`. A PUBACK goes to the
   broker only after Kafka has acknowledged the produced record (`acks=all`).
   Until then, the message lives in the bridge's **persistent MQTT session**
   (`CleanStart=false`, 1h expiry), so it's redelivered after any crash.
2. **In-order ack pipeline.** Each message is produced immediately (not
   awaited) and queued as `(message, deliveryTask)`. A single loop awaits
   deliveries in arrival order and acks them. Kafka work is pipelined while
   PUBACKs stay in PUBLISH order.
3. **Backpressure.** The queue is bounded (`MaxInFlight`, default 1000) and the
   bridge advertises the same MQTT v5 `ReceiveMaximum`. When Kafka stalls, the
   broker holds the backlog, not the bridge's memory.
4. **A Kafka failure ends the session.** When a delivery fails (`MessageTimeoutMs`
   = 60s), the session is torn down without acking and the bridge reconnects
   with backoff. The broker then redelivers everything unacked.
5. **Poison messages go to the DLQ and are acked.** Invalid JSON, a missing
   `messageId`/`batchId`, a device code that doesn't match the topic, or a
   physically impossible temperature is produced to `sensor-readings.dlq` with
   a `dlq-reason` header, then acked. Otherwise it would be redelivered forever.
   Business validation (does the batch exist?) is left to consumers.
6. **Pass-through payload.** The device's original bytes are forwarded unchanged,
   so fields this bridge doesn't know yet survive. Metadata goes in headers:
   `message-id`, `device-code`, `source`, `mqtt-topic`, `received-at-utc`.
7. **Horizontal scaling** uses an MQTT shared subscription
   (`$share/ingestion-bridge/...`). Each instance needs a unique, stable `InstanceId`.

## Verification (local, 2026-10-05)

| Scenario | Published | Distinct in Kafka | Duplicates |
|---|---|---|---|
| 5 devices at 25 msg/s, plus 3 poison messages | 414 + 3 | 414 (+3 in DLQ) | 0 |
| Kafka stopped for 80s mid-stream | 1212 | 1212 | 0 |
| 2 instances, one `kill -9`'d mid-stream | 644 | 644 | 0 |

During the outage, the broker buffered the backlog (the bridge held at most 52
messages). The first session failed after the 60s delivery timeout, and the
backlog drained at about 55 msg/s once Kafka was back.

## Consequences

- Delivery is at-least-once, not exactly-once. Duplicates were zero in these runs,
  but a crash between Kafka's ack and the PUBACK *will* produce one, so consumers
  must still dedupe on `message-id`.
- Ingestion latency during a Kafka outage is bounded only by the MQTT session
  expiry (1h) and HiveMQ's queue limits. Readings older than that are lost at the broker.
- Partition skew with few batches is visible (2 of 6 partitions were empty with 5
  batches). It evens out as the number of active batches grows.
