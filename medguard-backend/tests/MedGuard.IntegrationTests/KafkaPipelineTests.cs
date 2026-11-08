using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Events;
using MedGuard.Contracts.Kafka;
using MedGuard.Infrastructure.Persistence;
using MedGuard.Monitoring.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedGuard.IntegrationTests;

/// <summary>The parts that talk to a real Kafka: outbox publishing (ADR 0006) and the worker's batch path (ADR 0008).</summary>
[Collection(PipelineCollection.Name)]
public class KafkaPipelineTests(PipelineFixture fx)
{
    private static string? Header(ConsumeResult<string, byte[]> r, string key) =>
        r.Message.Headers?.TryGetLastBytes(key, out var b) == true ? Encoding.UTF8.GetString(b) : null;

    [Fact]
    public async Task Outbox_row_is_published_with_its_id_as_message_id_and_marked_processed()
    {
        var batch = await fx.SeedBatchAsync();
        Guid alertId;
        await using (var scope = fx.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>()
                .IngestAsync(new IngestReadingCommand(Guid.NewGuid(), batch.Id, "DEV-IT", 12m, null, null, null, DateTime.UtcNow));
            Assert.Equal(IngestOutcome.Recorded, result.Outcome);
            alertId = await scope.ServiceProvider.GetRequiredService<MedGuardDbContext>()
                .Alerts.Where(a => a.BatchId == batch.Id).Select(a => a.Id).SingleAsync();
        }

        var dispatcher = new OutboxDispatcher(
            fx.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions { PollIntervalMs = 100 }),
            Options.Create(new MonitoringOptions { KafkaBootstrapServers = fx.KafkaBootstrap }),
            NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            var outboxId = await WaitForAsync(async () =>
            {
                await using var scope = fx.Scope();
                var row = await scope.ServiceProvider.GetRequiredService<MedGuardDbContext>().OutboxMessages
                    .SingleAsync(m => m.MessageKey == batch.Id.ToString());
                return row.ProcessedAtUtc is null ? (Guid?)null : row.Id;
            });

            var messages = fx.ReadTopic(KafkaTopics.BatchAlerts, seen => seen.Any(m => m.Message.Key == batch.Id.ToString()));
            var published = Assert.Single(messages, m => m.Message.Key == batch.Id.ToString());

            Assert.Equal(outboxId.ToString(), Header(published, KafkaHeaders.MessageId));
            Assert.Equal(nameof(BatchAlertRaised), Header(published, KafkaHeaders.EventType));
            var evt = JsonSerializer.Deserialize<BatchAlertRaised>(published.Message.Value, ContractJson.Options)!;
            Assert.Equal(alertId, evt.AlertId);
            Assert.Equal("Critical", evt.Severity);
            Assert.Equal("Quarantined", evt.BatchStatus);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            dispatcher.Dispose();
        }
    }

    [Fact]
    public async Task Batch_handler_records_good_messages_and_dead_letters_bad_ones_with_a_reason()
    {
        var batch = await fx.SeedBatchAsync();
        var good = Telemetry(batch.Id, 5m);
        var orphan = Telemetry(Guid.NewGuid(), 5m);
        var messages = new[]
        {
            AsConsumed(0, JsonSerializer.SerializeToUtf8Bytes(good, ContractJson.Options), batch.Id.ToString()),
            AsConsumed(1, "{not json"u8.ToArray(), "garbage-key"),
            AsConsumed(2, JsonSerializer.SerializeToUtf8Bytes(orphan, ContractJson.Options), orphan.BatchId.ToString()),
            AsConsumed(3, JsonSerializer.SerializeToUtf8Bytes(good, ContractJson.Options), batch.Id.ToString()), // redelivery
        };

        using var dlqProducer = fx.CreateProducer();
        var handler = new TelemetryMessageHandler(fx.Services.GetRequiredService<IServiceScopeFactory>(), dlqProducer,
            new MonitoringOptions(), NullLogger<TelemetryMessageHandler>.Instance);

        var (doneCount, _) = await handler.HandleBatchAsync(messages, CancellationToken.None);

        Assert.Equal(messages.Length, doneCount);
        Assert.Equal(1, handler.Stats.Recorded);
        Assert.Equal(1, handler.Stats.Duplicates);
        Assert.Equal(2, handler.Stats.DeadLettered);
        Assert.Equal(0, handler.Stats.Fallbacks);

        var dlq = fx.ReadTopic(KafkaTopics.SensorReadingsDlq,
            seen => seen.Count(m => m.Message.Key is "garbage-key" || m.Message.Key == orphan.BatchId.ToString()) == 2);
        Assert.Contains(dlq, m => m.Message.Key == "garbage-key" && Header(m, KafkaHeaders.DlqReason) == "undeserializable");
        Assert.Contains(dlq, m => m.Message.Key == orphan.BatchId.ToString() && Header(m, KafkaHeaders.DlqReason)!.Contains("not found"));
    }

    private static DeviceTelemetry Telemetry(Guid batchId, decimal temp) =>
        new(Guid.NewGuid(), "DEV-IT", batchId, temp, 50m, null, null, DateTime.UtcNow);

    private static ConsumeResult<string, byte[]> AsConsumed(long offset, byte[] value, string key) => new()
    {
        Topic = KafkaTopics.SensorReadings,
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, byte[]> { Key = key, Value = value, Headers = new Headers() },
    };

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, int timeoutSeconds = 20) where T : struct
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } value) return value;
            await Task.Delay(200);
        }
        throw new TimeoutException("condition not met in time");
    }
}
