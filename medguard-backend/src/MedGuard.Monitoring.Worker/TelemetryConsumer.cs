using Confluent.Kafka;
using MedGuard.Contracts.Kafka;
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

    public TelemetryConsumer(IOptions<MonitoringOptions> options, TelemetryMessageHandler handler, ILogger<TelemetryConsumer> log)
    {
        _options = options.Value;
        _handler = handler;
        _log = log;
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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = consumer.Consume(TimeSpan.FromMilliseconds(500));

                if (DateTime.UtcNow >= nextReport)
                {
                    ReportStats(consumer);
                    nextReport = DateTime.UtcNow.AddSeconds(10);
                }

                if (message is null) continue;

                var (disposition, retryAfter) = await _handler.HandleAsync(message, ct);
                if (disposition == Disposition.Done)
                {
                    consumer.StoreOffset(message);
                }
                else
                {
                    // Rewind this partition to the failed message, so the next
                    // Consume() returns it again. We keep calling Consume() (no
                    // blocking sleep longer than 30s) because a consumer that stops
                    // polling for max.poll.interval.ms (5 min) is kicked out of the group.
                    consumer.Seek(message.TopicPartitionOffset);
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

        var s = _handler.Stats;
        _log.LogInformation(
            "partitions=[{Partitions}] lag={Lag} recorded={Recorded} warning={Warning} critical={Critical} duplicates={Duplicates} dlq={Dlq} retries={Retries} avg-db={Db} avg-dlq={DlqTime}",
            string.Join(",", consumer.Assignment.Select(p => p.Partition.Value).Order()), lag,
            s.Recorded, s.Warning, s.Critical, s.Duplicates, s.DeadLettered, s.Retries, s.Db.TakeAverage(), s.Dlq.TakeAverage());
    }
}
