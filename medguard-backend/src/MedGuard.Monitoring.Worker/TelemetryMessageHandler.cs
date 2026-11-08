using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Kafka;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;
using MedGuard.ServiceDefaults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Monitoring.Worker;

/// <summary>What the consumer loop should do with a message after handling it.</summary>
public enum Disposition
{
    /// <summary>Finished, whether recorded, skipped as a duplicate, or dead-lettered. Move the offset past it.</summary>
    Done,
    /// <summary>Not finished. Re-read the same offset later; don't move past it.</summary>
    Retry,
}

/// <summary>
/// Handles ONE Kafka message. Every failure is sorted into exactly one bucket,
/// because each bucket needs a different response:
///
///   duplicate      already processed         -> skip (Done)
///   poison         will NEVER succeed        -> dead-letter (Done)
///   transient      may succeed later         -> retry forever (Retry)
///   unknown        can't tell                -> retry a few times, then dead-letter
///
/// Getting this wrong is costly either way. Dead-letter a transient error and
/// you lose good readings every time the DB blips. Retry a poison message
/// forever and its whole partition (every batch on it) stops being monitored.
/// </summary>
public sealed class TelemetryMessageHandler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IProducer<string, byte[]> _dlqProducer;
    private readonly MonitoringOptions _options;
    private readonly ILogger<TelemetryMessageHandler> _log;
    private readonly Dictionary<TopicPartitionOffset, int> _attempts = new();

    public ConsumerStats Stats { get; } = new();

    public TelemetryMessageHandler(IServiceScopeFactory scopes, IProducer<string, byte[]> dlqProducer,
        MonitoringOptions options, ILogger<TelemetryMessageHandler> log)
    {
        _scopes = scopes;
        _dlqProducer = dlqProducer;
        _options = options;
        _log = log;
    }

    public async Task<(Disposition Disposition, TimeSpan RetryAfter)> HandleAsync(
        ConsumeResult<string, byte[]> message, CancellationToken ct)
    {
        DeviceTelemetry? telemetry;
        try
        {
            telemetry = JsonSerializer.Deserialize<DeviceTelemetry>(message.Message.Value, ContractJson.Options);
            if (telemetry is null) throw new JsonException("empty payload");
        }
        catch (JsonException ex)
        {
            return await DeadLetterAsync(message, $"undeserializable: {ex.Message}", ct);
        }

        var traceParent = PipelineTelemetry.TraceParentOf(message.Message.Headers);
        using var span = PipelineTelemetry.Source.StartActivity("process message", ActivityKind.Consumer,
            Contracts.Tracing.TraceContext.Parse(traceParent));
        span?.SetTag("messaging.system", "kafka");
        span?.SetTag("messaging.destination.partition.id", message.Partition.Value);

        try
        {
            // A fresh DI scope per message = a fresh DbContext per message. A
            // long-lived DbContext would keep tracking every entity it ever loaded
            // (growing memory), and one failed SaveChanges would poison it for
            // every message after.
            await using var scope = _scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>();

            var started = Stopwatch.GetTimestamp();
            var result = await service.IngestAsync(new IngestReadingCommand(
                telemetry.MessageId, telemetry.BatchId, telemetry.DeviceCode, telemetry.TemperatureC,
                telemetry.HumidityPercent, telemetry.Latitude, telemetry.Longitude, telemetry.RecordedAtUtc, traceParent), ct);
            Stats.Db.Add(Stopwatch.GetElapsedTime(started));
            PipelineTelemetry.DbDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            PipelineTelemetry.BatchSize.Record(1);

            switch (result.Outcome)
            {
                case IngestOutcome.Recorded:
                    Stats.Recorded++;
                    CountRecorded(telemetry);
                    CountAlert(result, message);
                    break;
                case IngestOutcome.Duplicate:
                    Stats.Duplicates++;
                    PipelineTelemetry.ReadingsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "duplicate"));
                    break;
                case IngestOutcome.BatchNotFound:
                    return await DeadLetterAsync(message, $"batch {telemetry.BatchId} not found", ct);
            }

            _attempts.Remove(message.TopicPartitionOffset);
            return (Disposition.Done, TimeSpan.Zero);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Lost the race on IX_SensorReadings_MessageId: someone else recorded it first.
            Stats.Duplicates++;
            return (Disposition.Done, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is DomainException or ArgumentException)
        {
            // The domain refused the reading outright (e.g. a critical breach on a
            // batch that's already Delivered can't be quarantined). Same input,
            // same refusal, every time.
            return await DeadLetterAsync(message, $"rejected by domain: {ex.Message}", ct);
        }
        catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
        {
            var attempt = NextAttempt(message);
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 30));
            Stats.Retries++;
            _log.LogWarning("Transient failure p{Partition}@{Offset} (attempt {Attempt}), retrying in {Delay}s: {Error}",
                message.Partition.Value, message.Offset.Value, attempt, delay.TotalSeconds, Innermost(ex).Message);
            return (Disposition.Retry, delay);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var attempt = NextAttempt(message);
            if (attempt >= _options.MaxAttemptsForUnknownErrors)
                return await DeadLetterAsync(message, $"failed {attempt}x: {Innermost(ex).Message}", ct);

            Stats.Retries++;
            _log.LogWarning(ex, "Unknown failure p{Partition}@{Offset} (attempt {Attempt})",
                message.Partition.Value, message.Offset.Value, attempt);
            return (Disposition.Retry, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Handles a batch in ONE transaction (ADR 0008). Returns how many messages,
    /// from the start of the list, are finished; the caller stores their offsets
    /// and re-reads the rest after <c>RetryAfter</c>. "A prefix is done" stays
    /// correct because the list keeps each partition's order.
    ///
    /// Failure handling keeps the single path's buckets:
    ///   per-message problems (bad JSON, unknown batch, domain rejection) -> DLQ that one message
    ///   transient (DB down)  -> nothing was committed: retry the whole batch
    ///   anything else        -> fall back to one-at-a-time for this batch, which
    ///                           sorts out the culprit with the per-message rules
    /// </summary>
    public async Task<(int DoneCount, TimeSpan RetryAfter)> HandleBatchAsync(
        IReadOnlyList<ConsumeResult<string, byte[]>> messages, CancellationToken ct)
    {
        if (messages.Count == 1)
        {
            var (disposition, retryAfter) = await HandleAsync(messages[0], ct);
            return (disposition == Disposition.Done ? 1 : 0, retryAfter);
        }

        // One span for the batch, LINKED (not parented) to every message's trace:
        // a span can't have 200 parents. Links are OpenTelemetry's answer for
        // batch processing. Each reading that raises an alert also gets its own
        // child span in its device trace (ColdChainMonitoringService).
        var links = messages
            .Select(m => Contracts.Tracing.TraceContext.Parse(PipelineTelemetry.TraceParentOf(m.Message.Headers)))
            .Where(c => c != default)
            .Select(c => new ActivityLink(c))
            .ToList();
        using var batchSpan = PipelineTelemetry.Source.StartActivity("process batch", ActivityKind.Consumer,
            parentContext: default, links: links);
        batchSpan?.SetTag("messaging.system", "kafka");
        batchSpan?.SetTag("messaging.batch.message_count", messages.Count);

        // 1. Parse. Bad JSON can't be fixed by retrying; note it, keep going.
        var parsed = new DeviceTelemetry?[messages.Count];
        var commands = new List<IngestReadingCommand>(messages.Count);
        var commandIndex = new int[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            commandIndex[i] = -1;
            try { parsed[i] = JsonSerializer.Deserialize<DeviceTelemetry>(messages[i].Message.Value, ContractJson.Options); }
            catch (JsonException) { }
            if (parsed[i] is not { } t) continue;

            commandIndex[i] = commands.Count;
            commands.Add(new IngestReadingCommand(t.MessageId, t.BatchId, t.DeviceCode, t.TemperatureC,
                t.HumidityPercent, t.Latitude, t.Longitude, t.RecordedAtUtc,
                PipelineTelemetry.TraceParentOf(messages[i].Message.Headers)));
        }

        // 2. One transaction for every parseable message.
        IReadOnlyList<IngestResult> results = [];
        if (commands.Count > 0)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>();
                var started = Stopwatch.GetTimestamp();
                results = await service.IngestBatchAsync(commands, ct);
                Stats.Db.Add(Stopwatch.GetElapsedTime(started), commands.Count);
                Stats.Batches++;
                PipelineTelemetry.DbDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                PipelineTelemetry.BatchSize.Record(commands.Count);
                _batchAttempt = 0;
            }
            catch (Exception ex) when (IsTransient(ex) && !IsUniqueViolation(ex) && !ct.IsCancellationRequested)
            {
                // Rolled back: nothing from this batch was saved. Retry all of it.
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, _batchAttempt++), 30));
                Stats.Retries++;
                _log.LogWarning("Transient failure on a batch of {Count}, retrying in {Delay}s: {Error}",
                    messages.Count, delay.TotalSeconds, Innermost(ex).Message);
                return (0, delay);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // E.g. a unique-index race with another instance: we can't tell
                // which message caused it. One at a time, each gets classified
                // on its own (duplicate / poison / retry).
                Stats.Fallbacks++;
                _log.LogWarning("Batch of {Count} failed ({Error}); falling back to one-at-a-time",
                    messages.Count, Innermost(ex).Message);
                for (var i = 0; i < messages.Count; i++)
                {
                    var (disposition, retryAfter) = await HandleAsync(messages[i], ct);
                    if (disposition == Disposition.Retry)
                        return (i, retryAfter);
                }
                return (messages.Count, TimeSpan.Zero);
            }
        }

        // 3. Committed. Count outcomes and dead-letter the per-message failures.
        //    If a DLQ produce fails, everything from there on is re-read later;
        //    re-recording is harmless (MessageId dedupe).
        for (var i = 0; i < messages.Count; i++)
        {
            string? dlqReason = null;
            if (commandIndex[i] < 0)
            {
                dlqReason = "undeserializable";
            }
            else
            {
                var result = results[commandIndex[i]];
                switch (result.Outcome)
                {
                    case IngestOutcome.Recorded:
                        Stats.Recorded++;
                        CountRecorded(parsed[i]!);
                        CountAlert(result, messages[i]);
                        break;
                    case IngestOutcome.Duplicate:
                        Stats.Duplicates++;
                        PipelineTelemetry.ReadingsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "duplicate"));
                        break;
                    case IngestOutcome.BatchNotFound:
                        dlqReason = $"batch {parsed[i]!.BatchId} not found";
                        break;
                    case IngestOutcome.Rejected:
                        dlqReason = $"rejected by domain: {result.Error}";
                        break;
                }
            }

            if (dlqReason is not null)
            {
                var (disposition, retryAfter) = await DeadLetterAsync(messages[i], dlqReason, ct);
                if (disposition == Disposition.Retry)
                    return (i, retryAfter);
            }
        }

        return (messages.Count, TimeSpan.Zero);
    }

    private int _batchAttempt;

    private static void CountRecorded(DeviceTelemetry telemetry)
    {
        PipelineTelemetry.ReadingsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "recorded"));
        // Device sampled it -> committed now. The same number loadtest/report.sql
        // computes after the fact, but live, as a histogram (p50/p95 in Grafana).
        PipelineTelemetry.EndToEndLatency.Record((DateTime.UtcNow - telemetry.RecordedAtUtc).TotalMilliseconds);
    }

    private void CountAlert(IngestResult result, ConsumeResult<string, byte[]> message)
    {
        if (result.RaisedAlert is not { } severity) return;
        if (severity == AlertSeverity.Critical) Stats.Critical++; else Stats.Warning++;

        // Debug, not Error: an alert is a business event that's already stored
        // and published (outbox). At Error level, the load test showed one log
        // line per breaching reading, hundreds per second (ADR 0008).
        _log.LogDebug("{Severity} p{Partition}@{Offset}: {Message}",
            severity, message.Partition.Value, message.Offset.Value, result.AlertMessage);
    }

    private async Task<(Disposition, TimeSpan)> DeadLetterAsync(
        ConsumeResult<string, byte[]> message, string reason, CancellationToken ct)
    {
        var headers = new Headers();
        foreach (var h in message.Message.Headers ?? new Headers())
            headers.Add(h.Key, h.GetValueBytes());
        headers.Add(KafkaHeaders.DlqReason, Encoding.UTF8.GetBytes(reason));
        // Exactly where it came from, so it can be found, fixed and replayed.
        headers.Add("dlq-stage", "monitoring"u8.ToArray());
        headers.Add("original-topic-partition-offset", Encoding.UTF8.GetBytes(message.TopicPartitionOffset.ToString()));

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _dlqProducer.ProduceAsync(KafkaTopics.SensorReadingsDlq, new Message<string, byte[]>
            {
                Key = message.Message.Key,
                Value = message.Message.Value,
                Headers = headers,
            }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Can't park it, so don't skip it either. Try the whole thing again.
            _log.LogWarning("DLQ produce failed ({Error}); will retry the message", ex.Message);
            return (Disposition.Retry, TimeSpan.FromSeconds(5));
        }

        Stats.Dlq.Add(Stopwatch.GetElapsedTime(started));
        Stats.DeadLettered++;
        PipelineTelemetry.ReadingsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "dlq"));
        _attempts.Remove(message.TopicPartitionOffset);
        _log.LogWarning("DLQ p{Partition}@{Offset}: {Reason}", message.Partition.Value, message.Offset.Value, reason);
        return (Disposition.Done, TimeSpan.Zero);
    }

    private int NextAttempt(ConsumeResult<string, byte[]> message)
    {
        var attempt = _attempts.GetValueOrDefault(message.TopicPartitionOffset) + 1;
        _attempts[message.TopicPartitionOffset] = attempt;
        return attempt;
    }

    private static bool IsUniqueViolation(Exception ex) =>
        Chain(ex).OfType<SqlException>().Any(s => s.Number is 2601 or 2627);

    /// <summary>The database (or the network to it) is down, slow, or deadlocked: the message is fine, the moment isn't.</summary>
    private static bool IsTransient(Exception ex) =>
        Chain(ex).Any(e => e is SqlException or TimeoutException) || ex is DbUpdateException { InnerException: SqlException };

    private static IEnumerable<Exception> Chain(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException) yield return ex;
    }

    private static Exception Innermost(Exception ex) => Chain(ex).Last();
}

public sealed class ConsumerStats
{
    public long Recorded, Warning, Critical, Duplicates, DeadLettered, Retries, Batches, Fallbacks;

    /// <summary>Time spent in the database (IngestAsync) and in DLQ produces.</summary>
    public readonly Timing Db = new(), Dlq = new();
}

/// <summary>Average duration since the last report. Measure, don't guess, where the time goes.</summary>
public sealed class Timing
{
    private long _count;
    private TimeSpan _total;

    public void Add(TimeSpan elapsed) { _count++; _total += elapsed; }

    /// <summary>One measurement covering <paramref name="items"/> messages, so the average stays per message.</summary>
    public void Add(TimeSpan elapsed, int items) { _count += items; _total += elapsed; }

    public string TakeAverage()
    {
        var avg = _count == 0 ? "-" : $"{_total.TotalMilliseconds / _count:0.0}ms";
        _count = 0;
        _total = TimeSpan.Zero;
        return avg;
    }
}
