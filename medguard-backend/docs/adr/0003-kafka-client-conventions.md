# ADR 0003: Kafka client conventions (producers and consumers)

- **Status:** Accepted
- **Date:** 2026-09-26

## Context

Before writing the bridge (a producer) and the monitoring worker (a consumer),
we ran experiments with `MedGuard.KafkaLab` against the local broker. Several
defaults turned out to be wrong for this pipeline, and the results are recorded
here so every service configures its client the same way.

## Findings (observed, not assumed)

1. **The same key can land on a different partition depending on the client.**
   librdkafka (.NET) defaults to a CRC32 partitioner, while Java clients use
   murmur2. `batch-A` went to partition 2 with CRC32 and to partition 1 with
   murmur2 and with the Java console producer.
2. **Keys are bytes, not strings.** A key typed through PowerShell picked up an
   invisible UTF-8 BOM and was routed to a different partition than the same
   visible text.
3. **Key skew is real.** With 3 keys over 3 partitions, murmur2 put two keys on
   the same partition and left one empty, so the consumer assigned to it sat idle.
4. **Eager rebalancing stops everyone.** On every join or leave, all members
   revoked all of their partitions before getting new ones.
5. **A crashed consumer blocks its partitions for about 45s** (`session.timeout.ms`).
   A clean `Close()` rebalanced within milliseconds, but after `kill -9` it took
   43.6s. A restarted consumer in the same group also waited ~45s for the dead
   member to expire before it got any partitions.
6. **Auto-commit redelivers after a crash.** When the process crashed within
   the 5s auto-commit interval, the restart reprocessed everything from offset 0.
   Manual commit after processing resumed at exactly the next offset.
7. **A synchronous commit per message is slow** (about 5ms each, versus ~0 with auto-commit).

## Decision

**Producers**
- `Partitioner = Murmur2Random`, so partitioning is compatible with Java/Kafka tooling.
- `Acks = All` and `EnableIdempotence = true`.
- Keys are produced from typed values (`Guid.ToString()`), never from free text.

**Consumers**
- `EnableAutoCommit = true` together with `EnableAutoOffsetStore = false`: after
  a message is fully processed (DB transaction committed), call `StoreOffset(result)`.
  The background commit then only ever commits offsets we have finished. This
  gives the safety of manual commit without a blocking round-trip per message.
- `PartitionAssignmentStrategy = CooperativeSticky`, so a rebalance moves only
  the partitions that have to move instead of stopping the whole group.
- Always `Close()` on shutdown, which sends LeaveGroup.
- Every consumer is idempotent. Offsets make redelivery rare, not impossible:
  a crash between the DB commit and `StoreOffset` still reprocesses that message.

**Topics** are created explicitly (`auto.create.topics.enable=false`), and a
typo'd topic name fails with `Unknown topic or partition`.

## Consequences

- A crashed worker still leaves its partitions unprocessed for up to
  `session.timeout.ms`. We accept that for now. Tuning it lower speeds up
  failover but causes false "dead" verdicts during GC pauses or slow processing.
- Partition count (6 for `sensor-readings`) caps consumer parallelism, and it
  can't be reduced later without re-creating the topic.
