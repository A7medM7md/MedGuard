using Confluent.Kafka;
using MedGuard.Contracts.Kafka;
using MedGuard.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace MedGuard.Monitoring.Worker;

/// <summary>
/// Consumes <c>sensor-readings</c> and records each reading through the same
/// <c>Batch.RecordReading</c> domain logic the HTTP endpoint uses.
///
/// Offset rule (ADR 0003): an offset is STORED only after its message is
/// finished (DB transaction committed, or parked in the DLQ). The background
/// auto-commit then only ever commits stored offsets. A crash at any point
/// re-delivers from the last finished message: at-least-once, with duplicates
/// absorbed by the MessageId check.
/// </summary>
public sealed class TelemetryConsumer : BackgroundService
{
    private readonly MonitoringOptions _options;
    private readonly TelemetryMessageHandler _handler;
    private readonly ILogger<TelemetryConsumer> _log;
    private readonly WorkerHealth _health;

    public TelemetryConsumer(IOptions<MonitoringOptions> options, TelemetryMessageHandler handler,
        ILogger<TelemetryConsumer> log, WorkerHealth health)
    {
        _options = options.Value;
        _handler = handler;
        _log = log;
        _health = health;
    }

    // Consume() is a BLOCKING call. Running it directly in ExecuteAsync would
    // block the host's startup (StartAsync waits for ExecuteAsync's first real
    // await), so the loop gets its own thread-pool task.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = _options.KafkaBootstrapServers,
            GroupId = _options.GroupId,
            ClientId = $"monitoring-{_options.InstanceId}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            SessionTimeoutMs = _options.SessionTimeoutMs,
        })
        // With CooperativeSticky these fire with only the partitions that MOVE;
        // everything else keeps being consumed through the rebalance.
        .SetPartitionsAssignedHandler((_, parts) =>
            _log.LogInformation("Assigned +[{Partitions}]", string.Join(",", parts.Select(p => p.Partition.Value))))
        .SetPartitionsRevokedHandler((_, parts) =>
            _log.LogInformation("Revoked -[{Partitions}]", string.Join(",", parts.Select(p => p.Partition.Value))))
        .SetErrorHandler((_, e) => _log.LogWarning("Kafka: {Reason}", e.Reason))
        .Build();

        consumer.Subscribe(KafkaTopics.SensorReadings);
        _log.LogInformation("Consuming '{Topic}' as group '{Group}', instance {Instance}",
            KafkaTopics.SensorReadings, _options.GroupId, _options.InstanceId);

        var nextReport = DateTime.UtcNow.AddSeconds(10);
        var batch = new List<ConsumeResult<string, byte[]>>(_options.MaxBatchSize);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                CollectBatch(consumer, batch);
                _health.LoopTick(); // liveness: proves the loop isn't stuck (ADR 0010)

                if (DateTime.UtcNow >= nextReport)
                {
                    ReportStats(consumer);
                    nextReport = DateTime.UtcNow.AddSeconds(10);
                }

                if (batch.Count == 0) continue;

                var (doneCount, retryAfter) = await _handler.HandleBatchAsync(batch, ct);

                // The finished prefix: store offsets (only the last one per
                // partition matters, but storing each is cheap and in order).
                for (var i = 0; i < doneCount; i++)
                    consumer.StoreOffset(batch[i]);

                if (doneCount == batch.Count) _health.Progressing(); else _health.Retrying();

                if (doneCount < batch.Count)
                {
                    // Rewind every partition to its first unfinished message, so
                    // the next Consume() returns them again. We keep calling
                    // Consume() (no blocking sleep longer than 30s) because a consumer
                    // that stops polling for max.poll.interval.ms (5 min) is kicked
                    // out of the group.
                    foreach (var first in batch.Skip(doneCount).GroupBy(m => m.TopicPartition).Select(g => g.First()))
                        consumer.Seek(first.TopicPartitionOffset);
                    await Task.Delay(retryAfter, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            // Commits the stored offsets and sends LeaveGroup, so the partitions
            // move to another instance immediately instead of after session.timeout.
            consumer.Close();
            _log.LogInformation("Left the consumer group cleanly");
        }
    }

    /// <summary>
    /// Blocks up to 500ms for the FIRST message, then tops the batch up for at most
    /// MaxBatchWaitMs. With a backlog, librdkafka has already prefetched thousands
    /// of messages, so the batch fills in microseconds. When traffic is quiet, a lone
    /// message waits at most 20ms for company.
    /// </summary>
    private void CollectBatch(IConsumer<string, byte[]> consumer, List<ConsumeResult<string, byte[]>> batch)
    {
        batch.Clear();
        var first = consumer.Consume(TimeSpan.FromMilliseconds(500));
        if (first is null) return;
        batch.Add(first);

        var deadline = DateTime.UtcNow.AddMilliseconds(_options.MaxBatchWaitMs);
        while (batch.Count < _options.MaxBatchSize)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var next = consumer.Consume(remaining);
            if (next is null) break;
            batch.Add(next);
        }
    }

    private long _lastDone;

    private void ReportStats(IConsumer<string, byte[]> consumer)
    {
        // Lag = how far behind the newest message we are. Its trend is THE
        // health signal of a consumer: flat near 0 is healthy, growing means we
        // can't keep up (or are stuck retrying).
        long lag = 0;
        foreach (var tp in consumer.Assignment)
        {
            var high = consumer.GetWatermarkOffsets(tp).High;
            var position = consumer.Position(tp);
            if (high != Offset.Unset && position != Offset.Unset)
                lag += Math.Max(0, high.Value - position.Value);
        }

        PipelineTelemetry.SetConsumerLag(lag);
        var s = _handler.Stats;
        // Messages FINISHED per second (recorded, duplicate or dead-lettered),
        // i.e. this instance's throughput. Compare it with the offered load.
        var done = s.Recorded + s.Duplicates + s.DeadLettered;
        var rate = (done - _lastDone) / 10.0;
        _lastDone = done;
        _log.LogInformation(
            "partitions=[{Partitions}] lag={Lag} rate={Rate:0.0}/s batches={Batches} fallbacks={Fallbacks} recorded={Recorded} warning={Warning} critical={Critical} duplicates={Duplicates} dlq={Dlq} retries={Retries} avg-db={Db} avg-dlq={DlqTime}",
            string.Join(",", consumer.Assignment.Select(p => p.Partition.Value).Order()), lag, rate, s.Batches, s.Fallbacks,
            s.Recorded, s.Warning, s.Critical, s.Duplicates, s.DeadLettered, s.Retries, s.Db.TakeAverage(), s.Dlq.TakeAverage());
    }
}
