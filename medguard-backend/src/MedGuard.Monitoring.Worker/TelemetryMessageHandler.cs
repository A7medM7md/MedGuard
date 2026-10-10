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
                telemetry.HumidityPercent, telemetry.Latitude, telemetry.Longitude, telemetry.RecordedAtUtc), ct);
            Stats.Db.Add(Stopwatch.GetElapsedTime(started));

            switch (result.Outcome)
            {
                case IngestOutcome.Recorded:
                    Stats.Recorded++;
                    if (result.RaisedAlert is { } severity)
                    {
                        if (severity == AlertSeverity.Critical) Stats.Critical++; else Stats.Warning++;
                        _log.Log(severity == AlertSeverity.Critical ? LogLevel.Error : LogLevel.Warning,
                            "{Severity} p{Partition}@{Offset}: {Message}",
                            severity, message.Partition.Value, message.Offset.Value, result.AlertMessage);
                    }
                    break;
                case IngestOutcome.Duplicate:
                    Stats.Duplicates++;
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
    public long Recorded, Warning, Critical, Duplicates, DeadLettered, Retries;

    /// <summary>Time spent in the database (IngestAsync) and in DLQ produces.</summary>
    public readonly Timing Db = new(), Dlq = new();
}

/// <summary>Average duration since the last report. Measure, don't guess, where the time goes.</summary>
public sealed class Timing
{
    private long _count;
    private TimeSpan _total;

    public void Add(TimeSpan elapsed) { _count++; _total += elapsed; }

    public string TakeAverage()
    {
        var avg = _count == 0 ? "-" : $"{_total.TotalMilliseconds / _count:0.0}ms";
        _count = 0;
        _total = TimeSpan.Zero;
        return avg;
    }
}
