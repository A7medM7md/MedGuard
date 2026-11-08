using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Kafka;
using MedGuard.Contracts.Tracing;
using MedGuard.Infrastructure.Persistence;
using MedGuard.Monitoring.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedGuard.IntegrationTests;

/// <summary>
/// ADR 0011's claim, checked: one trace id survives Kafka header → batch
/// handler → outbox row in SQL → dispatcher → Kafka header, across a
/// "committed now, published later" gap. No Seq needed: an ActivityListener
/// stands in for the OpenTelemetry SDK.
/// </summary>
[Collection(PipelineCollection.Name)]
public sealed class TracePropagationTests : IDisposable
{
    private static readonly ActivitySource Device = new("MedGuard.TestDevice");
    private readonly PipelineFixture _fx;
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;

    public TracePropagationTests(PipelineFixture fx)
    {
        _fx = fx;
        // Listen to every MedGuard source, like AddSource("MedGuard.*") does.
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name.StartsWith("MedGuard."),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (_spans) _spans.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task Device_trace_reaches_the_published_alert_through_the_batch_path_and_the_outbox()
    {
        var batch = await _fx.SeedBatchAsync();

        // The "device": a root span whose traceparent travels with the reading.
        string deviceTraceParent;
        ActivityTraceId deviceTraceId;
        using (var deviceSpan = Device.StartActivity("telemetry publish", ActivityKind.Producer)!)
        {
            deviceTraceParent = deviceSpan.Id!;
            deviceTraceId = deviceSpan.TraceId;
        }

        // Two messages so the BATCH path runs: one traced critical reading, one untraced safe one.
        var critical = new DeviceTelemetry(Guid.NewGuid(), "DEV-IT", batch.Id, 12m, null, null, null, DateTime.UtcNow);
        var safe = new DeviceTelemetry(Guid.NewGuid(), "DEV-IT", batch.Id, 5m, null, null, null, DateTime.UtcNow);
        var tracedHeaders = new Headers { { TraceContext.TraceParent, Encoding.ASCII.GetBytes(deviceTraceParent) } };
        var messages = new[]
        {
            Consumed(0, critical, tracedHeaders),
            Consumed(1, safe, new Headers()),
        };

        using var dlq = _fx.CreateProducer();
        var handler = new TelemetryMessageHandler(_fx.Services.GetRequiredService<IServiceScopeFactory>(), dlq,
            new MonitoringOptions(), NullLogger<TelemetryMessageHandler>.Instance);
        var (done, _) = await handler.HandleBatchAsync(messages, CancellationToken.None);
        Assert.Equal(2, done);

        // 1. The batch span LINKS to the device trace (it can't have it as a parent).
        var batchSpan = Assert.Single(Spans("process batch"));
        Assert.Contains(batchSpan.Links, l => l.Context.TraceId == deviceTraceId);

        // 2. The alerting reading got a span INSIDE the device trace.
        var readingSpan = Assert.Single(Spans("record reading"), s => s.Recorded);
        Assert.Equal(deviceTraceId, readingSpan.TraceId);

        // 3. The outbox row remembered that span: the trace survives being parked in SQL.
        string? storedTraceParent;
        await using (var scope = _fx.Scope())
            storedTraceParent = await scope.ServiceProvider.GetRequiredService<MedGuardDbContext>().OutboxMessages
                .Where(m => m.MessageKey == batch.Id.ToString()).Select(m => m.TraceParent).SingleAsync();
        Assert.Equal(deviceTraceId, TraceContext.Parse(storedTraceParent).TraceId);

        // 4. The dispatcher publishes it with a traceparent in the SAME trace.
        var dispatcher = new OutboxDispatcher(_fx.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions { PollIntervalMs = 100 }),
            Options.Create(new MonitoringOptions { KafkaBootstrapServers = _fx.KafkaBootstrap }),
            NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            var published = _fx.ReadTopic(KafkaTopics.BatchAlerts, seen => seen.Any(m => m.Message.Key == batch.Id.ToString()))
                .Single(m => m.Message.Key == batch.Id.ToString());
            Assert.True(published.Message.Headers.TryGetLastBytes(TraceContext.TraceParent, out var header));
            Assert.Equal(deviceTraceId, TraceContext.Parse(Encoding.ASCII.GetString(header)).TraceId);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            dispatcher.Dispose();
        }
    }

    private List<Activity> Spans(string name)
    {
        lock (_spans) return _spans.Where(s => s.DisplayName == name).ToList();
    }

    private static ConsumeResult<string, byte[]> Consumed(long offset, DeviceTelemetry t, Headers headers) => new()
    {
        Topic = KafkaTopics.SensorReadings,
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, byte[]>
        {
            Key = t.BatchId.ToString(),
            Value = JsonSerializer.SerializeToUtf8Bytes(t, ContractJson.Options),
            Headers = headers,
        },
    };
}
