# ADR 0001 — MQTT at the edge, Kafka in the backend for telemetry ingestion

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

Today every sensor reading is an HTTP `POST /api/SensorReadings`. The request
thread loads the batch, evaluates the reading, and writes to SQL Server before
responding. Receiving a reading and processing it are the same operation, so:

- Ingestion throughput is capped by database write latency.
- A slow or unavailable database means readings are rejected, and they are lost
  unless the device retries.
- A new consumer of readings (analytics, notifications) requires changing the
  ingestion code path.
- HTTP is a poor fit for constrained devices on flaky mobile links: each request
  pays for connection setup and headers, and there's no built-in "device went
  offline" signal.

## Decision

Split ingestion into two protocols, each doing what it's good at:

1. **MQTT (HiveMQ)** for device → cloud. Devices keep one persistent
   connection, publish with QoS 1 to `medguard/devices/{deviceCode}/telemetry`,
   and register a Last Will so the broker announces unexpected disconnects.
2. **Kafka** as the backend's durable log. A bridge worker consumes MQTT through
   a shared subscription and produces to the `sensor-readings` topic, keyed by
   `batchId`. It acknowledges the MQTT message only after Kafka confirms the write.
3. **Monitoring worker(s)** consume `sensor-readings` in a consumer group and run
   the existing `Batch.RecordReading` domain logic unchanged.

We write the bridge ourselves. HiveMQ's Kafka extension is Enterprise-only, and
building the bridge is the point of the exercise.

### Why `batchId` as the partition key

Kafka only guarantees order within a partition. `Batch` is the aggregate that
evaluates breaches and flips to Quarantined, so its readings have to be processed
in order by one consumer at a time. Keying by `batchId` provides exactly that.
Keying by `deviceCode` would let two devices on the same batch race each other.

## Consequences

- **Positive:** ingestion is decoupled from the database. Readings buffer in
  Kafka during a DB outage and are replayed afterwards. Processing scales
  horizontally, up to the partition count (6). New consumers can be added
  without touching ingestion.
- **Negative:** delivery is at-least-once end-to-end, so consumers must be
  idempotent. The system also becomes eventually consistent, since a reading is
  accepted before it's evaluated. There are more moving parts to run and observe.
- **Scope:** the pipeline runs locally via Docker Compose only. The hosted
  production environment (MonsterASP + Vercel) keeps the HTTP path. Moving to
  managed MQTT/Kafka will be decided in a later ADR.
