# ADR 0008: Load test, scaling, and batch consumption

- **Status:** Accepted (open items closed 2026-11-08, see "Follow-up runs")
- **Date:** 2026-11-08

## Context

ADR 0001 claimed the pipeline "scales horizontally, up to the partition count (6)".
That's a claim until it's measured. The target was 1000 devices × 1 reading/s.

## Method

- A dedicated `MedGuardLoadTest` database: 1000 batches with ids matching the
  simulator's (`loadtest/seed-batches.sql`). The dev database is untouched.
- Release builds. The simulator runs in `Quiet` mode (per-reading console output
  would make the load generator the bottleneck) and samples on a **fixed
  schedule**, so a slow broker can't quietly lower the offered load
  (coordinated omission).
- **End to end:** simulator → HiveMQ → bridge → Kafka → workers (first runs).
- **Drain test** (`loadtest/drain-test.sh`): `KafkaLab fill-readings` writes N
  readings straight to Kafka, the workers drain them, and throughput =
  committed / (last commit − first commit). It isolates the consumer and needs no
  HiveMQ, bridge or simulator, which matters on this machine.
- **Everything runs on one 7.7 GB laptop**: SQL Server, Kafka, the workers, plus
  a browser and an IDE. The absolute numbers belong to this machine; the
  *shapes* (what caps, what scales, what doesn't) are the findings. The drain
  script refuses to start below a RAM threshold, because a paging laptop
  measures its disk.

## Results

### End to end, 1000 devices × 1/s (no excursions)

| Run | Offered | Bridge → Kafka | Workers | `avg-db` |
|---|---|---|---|---|
| 1 worker | 1000/s | **~465/s** | 69.9/s | 13.8 ms |
| 2 workers | ~997/s | ~440/s | 2 × 55.7 = 111/s | 17.5 ms |

### Drain test (1000 keys, safe readings)

| Workers | `MaxBatchSize` | Throughput | Notes |
|---|---|---|---|
| 1 | 1 | **79–80/s** | two runs; `avg-db` 10.3 ms ≈ 1/rate |
| 2 | 1 | 104/s | 1.3×; agrees with the 111/s end-to-end run |
| 3 | 1 | 138/s | 1.75× |
| 8 | 1 | — | two members got **no partitions** (`+[] +[]`) |
| 3, **1 key** | 1 | **72/s** | 1 busy partition, **1 active worker**, 2 idle |
| 1 | **200** | **1622/s** | **20× one sequential worker**; 0.7 ms per message |

Runs with 6 sequential and 2+ batched workers were skipped: the machine had less
than 450 MB available by then.

## Findings

1. **Per-message cost beats consumer count.** Sequential processing is ~4 DB
   round trips per reading, so one worker ≈ 1 / `avg-db` (79/s at 10.3 ms;
   predicted before the run). Adding workers helped sub-linearly (3 workers =
   1.75×), because they share SQL Server and this laptop. **Batching gave 20× with
   one worker**, more than the 6-partition ceiling could ever give.
2. **Consumers beyond partitions do nothing:** 8 workers, 6 partitions → 2 idle members.
3. **Keys bound parallelism, not consumers:** with every reading on 1 batch,
   3 workers ran at the speed of 1. Keying by `batchId` (ADR 0001) is right
   for ordering, but a fleet that's mostly one huge batch would serialize. A
   1000-key spread put 4.0k–4.8k of lag on each of the 6 partitions.
4. **The bridge is now the end-to-end bottleneck (~465/s).** Its in-flight queue
   stayed at **0**, so it was waiting on the broker, not on Kafka. The unread ~36k
   messages sat **in HiveMQ's memory** (476 / 512 MiB), the least durable component
   in the chain, which is the opposite of ADR 0001's intent. Suspects, not yet
   separated: HiveMQ's memory limit, Docker Desktop's port-forwarding relay,
   and MQTTnet's one-at-a-time delivery to our handler.
5. **Per-alert logging at Error level** produced a log line per breaching reading
   (hundreds per second under excursions). It's now `Debug`: the alert is already
   stored and published.

### Measurement mistakes found along the way

Each one produced plausible-looking numbers. They're kept here because noticing
them was the real exercise.

| Symptom | Cause | Fix |
|---|---|---|
| "Reset" runs still contained old rows; a 5000-message drain recorded 11,839 | `sqlcmd` defaults `QUOTED_IDENTIFIER OFF`, so writes to tables with **filtered indexes** fail with Msg 1934, **and sqlcmd still exits 0** | `SET QUOTED_IDENTIFIER ON` in every script, `-b` on sqlcmd |
| Drain processed an old backlog | `reset-offsets` right after Kafka started hit "Coordinator load in progress" and failed silently | retry until `NEW-OFFSET` appears, otherwise abort |
| 3 workers "slower" than 2 (95 vs 180/s) | workers joined **during** the drain; each join is a rebalance pause inside the measurement | start the workers, wait for a stable assignment, *then* fill |

## Decision: batch consumption

`MaxBatchSize=200`, `MaxBatchWaitMs=20` (`MaxBatchSize=1` restores the old path).

- **Collect:** block up to 500ms for the first message, then top up for ≤20ms.
  Under backlog the batch fills instantly from librdkafka's prefetch; at low
  traffic it adds ≤20ms of latency.
- **One transaction:** `IColdChainMonitoringService.IngestBatchAsync` runs one
  existence query (`MessageId IN` via OPENJSON), one batch load (tracked),
  settings once, then the readings **in order** against the same tracked
  aggregates, and one `SaveChanges`. HTTP, single and batch all go through one
  private `ApplyAsync`, so there's one set of rules.
- **Offsets:** the handler returns how many messages, as a prefix, are finished.
  The consumer stores those and seeks each partition to its first unfinished one.
  A prefix is valid because the list keeps per-partition order.
- **Failures keep ADR 0005's buckets:** per-message problems (bad JSON, unknown
  batch, domain rejection) → DLQ that message, the rest commit. Transient →
  rolled back, retry the whole batch. Anything else (e.g. a unique-index race)
  → **fall back to one-at-a-time** for that batch, so the single path classifies
  the culprit.
- **Domain change it required:** `Batch.RecordReading` used to add the reading and
  alert *before* `Quarantine()` could throw (Delivered batch). With one DbContext
  per message that was harmless; in a shared unit of work it left half a reading
  tracked. It's now **all-or-nothing**: every check that can throw runs first,
  and then all state changes are applied together. Behaviour for single readings
  is unchanged.

### Batch-path correctness (one worker, `MaxBatchSize=200`)

200 critical readings over 10 batches, **every message sent twice**, batch
`SIM-0001` set to **Delivered**, plus 5 readings for a nonexistent batch:

| Check | Expected | Result |
|---|---|---|
| readings / distinct `MessageId` | 180 / 180 | 180 / 180 |
| alerts / outbox rows | 180 / 180 | 180 / 180 |
| batches quarantined | 9 | 9 |
| `SIM-0001` | still Delivered, 0 readings | Delivered, 0 readings |
| fallbacks | 0 | 0 |
| duplicates / DLQ | predicted 180 / 45 | **200 / 25** |

The prediction was wrong, and the actual result is the defensible one. A message's
id is marked as "seen" before its batch is looked up, so the second copy of a
**rejected** message in the same consumer batch counts as a duplicate, and the DLQ
gets one copy instead of two (20 rejected + 5 unknown = 25). If the copies land
in different consumer batches, both are dead-lettered, which is what the single
path does too.

## Follow-up runs (closing the open items)

### Is the bridge the ~465/s limit? No: it's upstream of it

1000 devices × 1/s, no workers needed (bridge only forwards to Kafka):

| Bridges on `$share/ingestion-bridge` | Devices published | Bridge 1 | Bridge 2 | **Total** |
|---|---|---|---|---|
| 1 | ~1000/s (PUBACK ~44 ms) | 370–450/s | — | **~410/s** |
| 2 | ~1000/s (PUBACK ~38 ms) | 400 messages, then **0/s** | 435–450/s | **~445/s** |

Doubling the bridges didn't raise the total. HiveMQ also sent almost everything
to **one** of the two shared subscribers, while the other sat idle with an empty
in-flight window. The cap lives in **HiveMQ CE or Docker Desktop's port relay**,
not in our bridge code. HiveMQ was again at its 512 MiB limit at the end, holding
the unread backlog. More bridges won't help. Next steps, if 1000/s end to end
matters: give HiveMQ more memory or try another broker (EMQX, Mosquitto), and
run the bridge inside the Docker network to take the port relay out of the path.

### Batched scaling (drain test, 40k readings)

| Workers | `MaxBatchSize` | Throughput |
|---|---|---|
| 1 | 200 | 3578/s |
| 2 | 200 | 5686/s (1.6×) |
| 3 | 200 | 6275/s (1.75×; +10% over 2) |

As predicted, SQL Server becomes the limit: the 3rd worker adds 10%.
The 1-worker figure is **3578/s here vs 1622/s in the first run**: same code path,
different conditions (40k instead of 20k messages, so warm-up weighs less;
~1.3 GB of RAM free instead of ~0.7 GB). Treat absolute numbers on this laptop as
±2×. The *ratios* (batching ≫ sequential; sub-linear scaling) held in every run.

**End to end, the pipeline is capped at the broker (~450/s)**, far below what
the workers can absorb. The bottleneck moved from the consumer (phase 6) to the edge.

## Consequences / open items

- ~~Bridge cap~~: measured above. It's the broker/Docker path, not the bridge.
- ~~Batched scaling~~: measured above. SQL Server is the limit from about 2–3 workers.
- **Hot batches:** if one batch can ever carry a large share of devices, consider
  keying by `batchId` + device shard and reordering per batch downstream. Not
  needed for today's data.
