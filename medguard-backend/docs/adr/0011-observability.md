# ADR 0011: Observability (traces, metrics, logs)

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

Phase 6 measured the pipeline with grep over log lines and SQL queries after the
fact. Production questions need live answers: *Why was this alert 4 seconds
late? Which hop? Is lag growing right now?* A reading crosses 6 processes and 3
protocols, so per-service logs can't answer them.

## Decision

All of it lives in `MedGuard.ServiceDefaults`: one `AddServiceDefaults(serviceName)`
call per service. It's **off by default** (`Observability:Enabled`) and on in the
dev launch profiles. The stack runs with `docker compose --profile observability up -d`.

| Signal | Tool | Path |
|---|---|---|
| Logs | Serilog → console + **Seq** | every event carries TraceId/SpanId → one click to its trace |
| Traces | OpenTelemetry → OTLP/HTTP → **Seq** | `/ingest/otlp/v1/traces` |
| Metrics | OpenTelemetry → OTLP push → **Prometheus** (`--web.enable-otlp-receiver`) → **Grafana** | provisioned "MedGuard pipeline" dashboard |

Seq does logs *and* traces in one container (~0.5 GB), which matters on this machine.

### One trace from the sensor to the browser

The W3C `traceparent` crosses every hop:

```
simulator     root span; traceparent in an MQTT v5 USER PROPERTY (payload untouched)
bridge        "telemetry forward": child of it; injects traceparent as a Kafka header
worker        "process batch": LINKED to every message's trace (a span can't have 200 parents)
              "record reading": child of the device trace, per reading
              -> the outbox captures it: OutboxMessage.TraceParent (new column)
dispatcher    "batch-alerts publish": child of the stored traceparent, possibly
              minutes later in another process; injects it into the Kafka header
notifier      "push alert": child of it; sends TraceId with the SignalR push
browser       the alert row's tooltip shows the trace id: paste it into Seq
```

- **BCL-only propagation** (`Activity.Id`, `ActivityContext.TryParse`, in
  `Contracts.Tracing`): the Application layer emits spans without referencing
  OpenTelemetry. With no listener, `StartActivity` returns null and all of it costs nothing.
- **Noise control:** at 1000+ readings/s, a span per safe reading would swamp the
  trace store. A reading's span is exported **only if it raised an alert**: the
  Recorded flag is cleared otherwise, so the exporter drops it. The simulator
  samples roots (`Observability:TraceSampleRatio`, e.g. 0.01 under load).
  Unsampled readings carry no traceparent at all.
- Spans that measure an async delivery end **on the broker ack**, and are started
  without becoming `Activity.Current`. Otherwise the next row's span would become
  their child.

### Metrics (Meter `MedGuard.Pipeline`)

Dimensions are small fixed sets only (`outcome`, `result`), never ids.

| Metric | Answers |
|---|---|
| `medguard.bridge.messages{outcome}` | offered load, bridge DLQ |
| `medguard.readings.processed{outcome}` | worker throughput: recorded / duplicate / dlq |
| `medguard.readings.e2e_latency` (ms histogram, custom buckets) | device → SQL p50/p95/p99, i.e. `report.sql`, live |
| `medguard.consumer.lag` (gauge, per instance) | THE consumer health signal |
| `medguard.worker.batch_size`, `.db_duration` | is batching working, what does a transaction cost |
| `medguard.outbox.published{result}` | alert path health |
| `medguard.notifier.alerts{result}`, `.reading_pushes` | pushed vs throttled vs duplicate |

## Verified

- Trace propagation through the batch path, SQL outbox and dispatcher: automated
  (`TracePropagationTests`, real SQL + Kafka, an ActivityListener standing in for the SDK).
- All services start with observability enabled and no collector reachable (the
  exporters fail quietly). docker-compose validates with the profile.
  Dashboard JSON validates.

**Not verified:** the Seq, Prometheus and Grafana containers haven't run on this
machine yet (RAM; earlier Docker Hub pulls timed out). In particular, the
dashboard's metric names assume Prometheus' default OTLP translation (e.g.
`medguard_readings_e2e_latency_milliseconds_bucket`). If a panel is empty, check
the real name in Prometheus' UI first.

## Consequences

- The stack is ~1 GB more RAM, so it's opt-in.
- `OutboxMessages.TraceParent` needs the `AddOutboxTraceParent` migration
  (nullable; old rows simply aren't traced).
- The browser itself isn't instrumented (no OpenTelemetry JS); the trace id in
  the push is the bridge between the two. Real browser spans would be the next step.
