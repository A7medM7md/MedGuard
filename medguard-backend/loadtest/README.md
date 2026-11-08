# Load testing the telemetry pipeline

Findings and numbers are in [ADR 0008](../docs/adr/0008-load-test-and-scaling.md).
This file covers how to reproduce them.

## 0. One-time setup

A separate database, so a million test readings never land in `MedGuardDb`:

```bash
CS='Server=localhost,1433;Database=MedGuardLoadTest;User Id=sa;Password=<pwd>;TrustServerCertificate=True;'
dotnet ef database update --project src/MedGuard.Infrastructure --startup-project src/MedGuard.API --connection "$CS"

# 1000 batches whose ids match what the simulator sends (Git Bash: export MSYS_NO_PATHCONV=1 first)
docker exec -i dev-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<pwd>' -C \
  -d MedGuardLoadTest -v Count=1000 -i /dev/stdin < loadtest/seed-batches.sql
```

Build **Release** (`dotnet build -c Release`) and run the dlls from `bin/Release/net8.0`.
Debug builds and `dotnet run` measure the wrong thing.

## 1a. Drain test (consumer throughput only; light on RAM)

Needs only SQL Server + Kafka. It fills the topic directly, then times the workers:

```bash
dotnet build MedGuard.sln -c Release
SA_PASSWORD=... loadtest/drain-test.sh <workers> <batch-size> [count=20000] [keys=1000]

loadtest/drain-test.sh 1 1 5000        # sequential baseline
loadtest/drain-test.sh 1 200 20000     # batched
loadtest/drain-test.sh 3 1 3000 1      # hot key: 3 workers, 1 batch
```

It refuses to run under 700 MB of available RAM (`MIN_AVAIL_MB` overrides that)
and prints RAM before and after next to each result.

## 1b. End-to-end run (MQTT included)

```bash
# clean slate: results wiped, batches back to Active, group moved past old messages
sqlcmd ... -i loadtest/reset.sql
docker exec medguard-kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 \
  --group monitoring --reset-offsets --to-latest --topic sensor-readings --execute

# terminal 1: bridge
dotnet MedGuard.IngestionBridge.dll

# terminals 2..N+1: workers (unique InstanceId each; only ONE runs the outbox)
dotnet MedGuard.Monitoring.Worker.dll --Monitoring:InstanceId=1 "--ConnectionStrings:MedGuardDb=$CS"
dotnet MedGuard.Monitoring.Worker.dll --Monitoring:InstanceId=2 "--ConnectionStrings:MedGuardDb=$CS" --Outbox:Enabled=false

# last terminal: the load
dotnet MedGuard.DeviceSimulator.dll --Simulation:DeviceCount=1000 --Simulation:IntervalSeconds=1 \
  --Simulation:ExcursionChancePercent=0 --Simulation:Quiet=true
```

## 2. What to watch

| Where | Signal |
|---|---|
| simulator `[sim]` line | `rate` = offered load, `avg-puback` = broker health |
| bridge log | `rate` should equal the simulator's; `in-flight` near `MaxInFlight` = Kafka is the bottleneck |
| worker log | `rate` = this instance's throughput, `lag` = its backlog, `avg-db` = cost per message |
| `kafka-consumer-groups.sh --describe --group monitoring` | lag **per partition**, and which instance owns it |
| Kafka UI (`docker compose --profile tools up -d`, then :8085) | the same, as graphs |
| `loadtest/report.sql` | committed rate and device→DB p50/p95 per 10s window |

## 3. Experiments worth repeating

| Knob | Lesson |
|---|---|
| workers 1 → 2 → 3 → 6 | throughput scales with consumers... |
| workers 8 (6 partitions) | ...until consumers = partitions; the extra 2 sit idle |
| `--Simulation:BatchCount=3` with 6 workers | hot keys: 3 batches → ≤3 busy partitions, half the workers idle |
| `ExcursionChancePercent=3` | alert path: outbox rows, a log line per alert, notifier throttling |

**Gotchas (each one cost a wrong result, see ADR 0008):**

- `sqlcmd` defaults `QUOTED_IDENTIFIER OFF`. Writes to tables with filtered indexes
  then fail **with exit code 0**. The scripts set it ON; do the same in ad-hoc queries.
- Start workers **before** filling the topic. Members joining mid-drain cause
  rebalance pauses that end up inside the measurement.
- Right after Kafka starts, `reset-offsets` can fail ("Coordinator load in
  progress"). Check that it printed `NEW-OFFSET`.
- If a worker is killed instead of stopped with Ctrl+C, its partitions
stay assigned to the dead member until `session.timeout.ms` (45s by default)
runs out. Wait that long before starting the next run, or pass
`--Monitoring:SessionTimeoutMs=10000`.
