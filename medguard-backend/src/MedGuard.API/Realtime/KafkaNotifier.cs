using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Confluent.Kafka;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Events;
using MedGuard.Contracts.Kafka;
using MedGuard.Contracts.Tracing;
using MedGuard.ServiceDefaults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MedGuard.API.Realtime;

/// <summary>
/// Kafka → SignalR (ADR 0007). Consumes <c>batch-alerts</c> and
/// <c>sensor-readings</c> and pushes them to every browser connected to
/// <see cref="MonitoringHub"/>.
///
/// <b>Fan-out, not competing consumers.</b> The monitoring workers share ONE group,
/// so each reading is processed once. Here every API instance has its own
/// connected browsers, so every instance needs EVERY message. Each process
/// therefore joins with a unique group id: N API instances = N groups =
/// everyone gets everything.
///
/// <b>No offset commits, start at Latest.</b> A notification about a reading from
/// ten minutes ago, delivered after a restart, is noise. The page loads its
/// current state over HTTP when it connects, and pushes only carry what changes
/// after that. The push is a hint; the database stays the source of truth.
/// </summary>
public sealed class KafkaNotifier : BackgroundService
{
    private readonly NotifierOptions _options;
    private readonly IHubContext<MonitoringHub, IMonitoringClient> _hub;
    private readonly ILogger<KafkaNotifier> _log;
    private readonly AlertGate _alerts;
    private readonly NotifierHealth _health;

    // Conflation: only the newest reading per batch survives until the next flush.
    // 1000 devices x 1 reading/s becomes at most one update per batch per second,
    // whatever the device count. The browser can't render faster than that anyway.
    private readonly Dictionary<Guid, LiveReading> _pendingReadings = new();
    private long _alertsPushed, _readingsReceived, _readingFlushes, _badMessages;

    public KafkaNotifier(IOptions<NotifierOptions> options, IHubContext<MonitoringHub, IMonitoringClient> hub, NotifierHealth health,
        ILogger<KafkaNotifier> log)
    {
        _options = options.Value;
        _hub = hub;
        _log = log;
        _health = health;
        _alerts = new AlertGate(
            TimeSpan.FromMinutes(_options.DedupeWindowMinutes),
            TimeSpan.FromSeconds(_options.AlertThrottleSeconds));
    }

    // Consume() blocks, so it gets its own thread instead of blocking host startup.
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => RunAsync(ct), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        var groupId = $"notifier-{Environment.MachineName}-{Guid.NewGuid():N}";
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = _options.KafkaBootstrapServers,
            GroupId = groupId,
            ClientId = $"api-notifier-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            // Nothing to resume from: a new group id every start, and we always
            // want "from now". Committing would only leave clutter behind.
            EnableAutoCommit = false,
        })
        .SetErrorHandler((_, e) =>
        {
            _log.LogWarning("Kafka: {Reason}", e.Reason);
            if (e.Code == ErrorCode.Local_AllBrokersDown || e.IsFatal)
                _health.BrokerDown();
        })
        .Build();

        consumer.Subscribe([KafkaTopics.BatchAlerts, KafkaTopics.SensorReadings]);
        _log.LogInformation("Notifier consuming [{Alerts}, {Readings}] as group {Group}",
            KafkaTopics.BatchAlerts, KafkaTopics.SensorReadings, groupId);

        var nextFlush = DateTime.UtcNow.AddMilliseconds(_options.ReadingFlushIntervalMs);
        var nextHeartbeat = DateTime.UtcNow;
        var nextReport = DateTime.UtcNow.AddSeconds(30);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // A short timeout so the flush below still runs when traffic stops.
                var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                _health.LoopTick(consumer.Assignment.Count);

                if (DateTime.UtcNow >= nextHeartbeat)
                {
                    await _hub.Clients.All.FeedHeartbeat(new FeedStatus(_health.KafkaHealthy, consumer.Assignment.Count, DateTime.UtcNow));
                    nextHeartbeat = DateTime.UtcNow.AddSeconds(5);
                }
                if (result is not null)
                    await HandleAsync(result, ct);

                if (DateTime.UtcNow >= nextFlush)
                {
                    await FlushReadingsAsync(ct);
                    nextFlush = DateTime.UtcNow.AddMilliseconds(_options.ReadingFlushIntervalMs);
                }

                if (DateTime.UtcNow >= nextReport)
                {
                    _log.LogInformation(
                        "notifier alerts-pushed={Pushed} duplicates={Dup} throttled={Throttled} readings-in={In} flushes={Flushes} bad={Bad}",
                        _alertsPushed, _alerts.Duplicates, _alerts.Throttled, _readingsReceived, _readingFlushes, _badMessages);
                    nextReport = DateTime.UtcNow.AddSeconds(30);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken ct)
    {
        try
        {
            switch (result.Topic)
            {
                case KafkaTopics.BatchAlerts:
                    var alert = JsonSerializer.Deserialize<BatchAlertRaised>(result.Message.Value, ContractJson.Options)!;
                    // No header (shouldn't happen with the outbox) = can't dedupe;
                    // fall back to the alert's own id, which is just as unique.
                    var messageId = Header(result, KafkaHeaders.MessageId) is { } h && Guid.TryParse(h, out var id)
                        ? id : alert.AlertId;

                    // Last hop of the reading's trace: device -> bridge -> worker ->
                    // outbox -> here -> browser (ADR 0011).
                    using (var span = PipelineTelemetry.Source.StartActivity("push alert", ActivityKind.Consumer,
                               TraceContext.Parse(PipelineTelemetry.TraceParentOf(result.Message.Headers))))
                    {
                        var duplicatesBefore = _alerts.Duplicates;
                        if (_alerts.Admit(messageId, alert) is { } notification)
                        {
                            await _hub.Clients.All.AlertRaised(notification with { TraceId = span?.TraceId.ToString() });
                            _alertsPushed++;
                            PipelineTelemetry.NotifierAlerts.Add(1, new KeyValuePair<string, object?>("result", "pushed"));
                        }
                        else
                        {
                            var outcome = _alerts.Duplicates > duplicatesBefore ? "duplicate" : "throttled";
                            span?.SetTag("medguard.notifier_result", outcome);
                            PipelineTelemetry.NotifierAlerts.Add(1, new KeyValuePair<string, object?>("result", outcome));
                        }
                    }
                    break;

                case KafkaTopics.SensorReadings:
                    var t = JsonSerializer.Deserialize<DeviceTelemetry>(result.Message.Value, ContractJson.Options)!;
                    _readingsReceived++;
                    // Keep the newest by DEVICE time: a device flushing an hour of
                    // backlog after reconnecting mustn't overwrite the live value.
                    if (!_pendingReadings.TryGetValue(t.BatchId, out var existing) || t.RecordedAtUtc >= existing.RecordedAtUtc)
                        _pendingReadings[t.BatchId] = new LiveReading(t.MessageId, t.BatchId, t.DeviceCode, t.TemperatureC,
                            t.HumidityPercent, t.Latitude, t.Longitude, t.RecordedAtUtc);
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentNullException or NullReferenceException)
        {
            // Bad payloads are the monitoring worker's problem (it dead-letters
            // them). For a best-effort notification, skipping is the right call.
            _badMessages++;
            _log.LogDebug(ex, "Skipping unreadable message on {Topic} @{Offset}", result.Topic, result.Offset);
        }
    }

    private async Task FlushReadingsAsync(CancellationToken ct)
    {
        if (_pendingReadings.Count == 0) return;

        var batch = _pendingReadings.Values.ToList();
        _pendingReadings.Clear();
        await _hub.Clients.All.ReadingsUpdated(batch);
        _readingFlushes++;
        PipelineTelemetry.NotifierReadingPushes.Add(1);
    }

    private static string? Header(ConsumeResult<string, byte[]> r, string key) =>
        r.Message.Headers?.TryGetLastBytes(key, out var bytes) == true ? Encoding.UTF8.GetString(bytes) : null;
}
