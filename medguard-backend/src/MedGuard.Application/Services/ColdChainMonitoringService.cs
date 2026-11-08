using System.Diagnostics;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Contracts.Events;
using MedGuard.Contracts.Kafka;
using MedGuard.Contracts.Tracing;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Exceptions;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class ColdChainMonitoringService : ResponseHandler, IColdChainMonitoringService
{
    private readonly IUnitOfWork _uow;
    private readonly IOutbox _outbox;

    /// <summary>BCL ActivitySource: this layer emits spans without depending on OpenTelemetry (ADR 0011).</summary>
    private static readonly ActivitySource Tracing = new("MedGuard.Application");

    public ColdChainMonitoringService(IUnitOfWork uow, IOutbox outbox)
    {
        _uow = uow;
        _outbox = outbox;
    }

    public async Task<Response<SensorReadingDto>> RecordReadingAsync(RecordReadingRequest request, CancellationToken ct = default)
    {
        var batch = await _uow.Batches.GetByIdAsync(request.BatchId, ct)
            ?? throw new NotFoundException(nameof(Batch), request.BatchId);

        var reading = new SensorReading(batch.Id,
            request.DeviceId,
            request.TemperatureC,
            request.HumidityPercent,
            request.Latitude,
            request.Longitude);

        await RecordAsync(batch, reading, ct);

        var result = new SensorReadingDto(reading.Id, reading.BatchId, reading.DeviceId, reading.TemperatureC,
            reading.HumidityPercent, reading.Latitude, reading.Longitude, reading.RecordedAtUtc);

        return Success(result);
    }

    public async Task<IngestResult> IngestAsync(IngestReadingCommand command, CancellationToken ct = default)
    {
        // Fast path for redeliveries. Not race-proof on its own: two deliveries in
        // flight at once can both see "false". The unique index on MessageId is
        // the real guarantee, and the consumer treats its violation as Duplicate.
        if (await _uow.SensorReadings.ExistsByMessageIdAsync(command.MessageId, ct))
            return IngestResult.Duplicate;

        var batch = await _uow.Batches.GetByIdAsync(command.BatchId, ct);
        if (batch is null)
            return IngestResult.BatchNotFound;

        using var span = StartReadingSpan(command);
        var alert = await RecordAsync(batch, ToReading(command), ct);
        KeepSpanOnlyIfAlert(span, alert);
        return new IngestResult(IngestOutcome.Recorded, alert?.Severity, alert?.Message);
    }

    public async Task<IReadOnlyList<IngestResult>> IngestBatchAsync(IReadOnlyList<IngestReadingCommand> commands, CancellationToken ct = default)
    {
        // 3 queries for the whole batch instead of 3 per reading.
        var recorded = (await _uow.SensorReadings.GetExistingMessageIdsAsync(
            commands.Select(c => c.MessageId).Distinct().ToList(), ct)).ToHashSet();
        var batches = (await _uow.Batches.GetByIdsAsync(
            commands.Select(c => c.BatchId).Distinct().ToList(), ct)).ToDictionary(b => b.Id);
        var (warningMarginPercent, autoQuarantineOnBreach) = await GetThresholdsAsync(ct);

        var results = new IngestResult[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];

            // Add() is false for ids already in the DB AND for a second copy of
            // the same message inside this very batch (a redelivery can land in
            // the same poll as the original).
            if (!recorded.Add(command.MessageId))
            {
                results[i] = IngestResult.Duplicate;
                continue;
            }

            if (!batches.TryGetValue(command.BatchId, out var batch))
            {
                results[i] = IngestResult.BatchNotFound;
                continue;
            }

            using var span = StartReadingSpan(command);
            try
            {
                // In order, against the SAME tracked Batch: reading 7 sees the
                // quarantine that reading 3 caused, exactly as it would have
                // one at a time. Same partition = same batch = same order (ADR 0001).
                var alert = await ApplyAsync(batch, ToReading(command), warningMarginPercent, autoQuarantineOnBreach, ct);
                KeepSpanOnlyIfAlert(span, alert);
                results[i] = new IngestResult(IngestOutcome.Recorded, alert?.Severity, alert?.Message);
            }
            catch (DomainException ex)
            {
                // Safe to carry on only because RecordReading is all-or-nothing:
                // it threw before touching the aggregate, so nothing half-done is tracked.
                results[i] = IngestResult.Rejected(ex.Message);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return results;
    }

    /// <summary>
    /// A span per reading, parented to the DEVICE's trace (the traceparent that
    /// rode along through MQTT and Kafka) and linked to the current batch span.
    /// While it's Activity.Current, the outbox captures it, so an alert's journey
    /// to the browser continues the same trace.
    /// </summary>
    private static Activity? StartReadingSpan(IngestReadingCommand command)
    {
        var parent = TraceContext.Parse(command.TraceParent);
        if (parent == default) return null; // untraced reading: nothing to continue

        ActivityLink[]? links = Activity.Current is { } batchSpan ? [new ActivityLink(batchSpan.Context)] : null;
        var span = Tracing.StartActivity("record reading", ActivityKind.Internal, parent, links: links);
        span?.SetTag("medguard.batch_id", command.BatchId);
        span?.SetTag("medguard.message_id", command.MessageId);
        return span;
    }

    /// <summary>
    /// At 1000+ readings/s, a span for every safe reading would drown the trace
    /// store in noise. Clearing the Recorded flag before the span ends means the
    /// exporter drops it; only readings that raised an alert stay visible.
    /// </summary>
    private static void KeepSpanOnlyIfAlert(Activity? span, Alert? alert)
    {
        if (span is null) return;
        if (alert is null)
            span.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        else
            span.SetTag("medguard.alert_severity", alert.Severity.ToString());
    }

    private static SensorReading ToReading(IngestReadingCommand command) => new(command.BatchId,
        command.DeviceCode,
        command.TemperatureC,
        command.HumidityPercent,
        command.Latitude,
        command.Longitude,
        command.RecordedAtUtc,
        command.MessageId);

    private async Task<(decimal WarningMarginPercent, bool AutoQuarantineOnBreach)> GetThresholdsAsync(CancellationToken ct)
    {
        // Warning-margin and auto-quarantine are org-configurable (Settings page) —
        // read the live values rather than hardcoding them here.
        var settings = await _uow.OrgSettings.GetSingletonAsync(ct);
        return (settings?.WarningMarginPercent ?? 15m, settings?.AutoQuarantineOnBreach ?? true);
    }

    /// <summary>Shared by the single-reading entry points (HTTP and Kafka): apply, then save.</summary>
    private async Task<Alert?> RecordAsync(Batch batch, SensorReading reading, CancellationToken ct)
    {
        var (warningMarginPercent, autoQuarantineOnBreach) = await GetThresholdsAsync(ct);
        var alert = await ApplyAsync(batch, reading, warningMarginPercent, autoQuarantineOnBreach, ct);

        // One SaveChanges = one transaction: the reading, its alert, the batch's
        // new status and the outbox event are committed together or not at all.
        await _uow.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// The one place a reading meets the domain, used by EVERY path (HTTP, Kafka
    /// single, Kafka batch), so they all obey exactly the same rules. Stages
    /// changes only; the caller decides when to SaveChanges.
    /// </summary>
    private async Task<Alert?> ApplyAsync(Batch batch, SensorReading reading,
        decimal warningMarginPercent, bool autoQuarantineOnBreach, CancellationToken ct)
    {
        // All breach-evaluation and alert-raising logic lives inside the aggregate
        // now — this service just hands the reading (and the current settings) to
        // the batch and persists.
        var alert = batch.RecordReading(reading, warningMarginPercent, autoQuarantineOnBreach);

        await _uow.SensorReadings.AddAsync(reading, ct);
        // Alert.Id is assigned client-side (BaseEntity generates it in its constructor), so
        // if it's only reachable via batch.Alerts, EF's graph fixup sees a non-default key
        // and assumes it already exists — tracking it as Modified instead of Added, which
        // then fails as a no-op UPDATE. Adding it explicitly forces the correct Added state.
        if (alert is not null)
        {
            await _uow.Alerts.AddAsync(alert, ct);

            // Outbox (ADR 0006): staged here, written by the SaveChanges below.
            // Publishing to Kafka directly instead would open two failure windows:
            //   publish, then crash before commit -> UI shows an alert the DB never stored
            //   commit, then crash before publish -> alert stored, nobody is ever notified
            // Keyed by BatchId, like sensor-readings, so one batch's alerts stay in order.
            _outbox.Enqueue(KafkaTopics.BatchAlerts, batch.Id.ToString(), new BatchAlertRaised(
                alert.Id,
                batch.Id,
                batch.BatchNumber,
                batch.DrugName,
                alert.Severity.ToString(),
                alert.Message,
                reading.TemperatureC,
                batch.Status.ToString(),
                reading.DeviceId,
                alert.TriggeredAtUtc));
        }

        return alert;
    }

    public async Task<Response<List<SensorReadingDto>>> GetHistoryAsync(Guid batchId, int take = 100, CancellationToken ct = default)
    {
        var readings = await _uow.SensorReadings.GetByBatchIdAsync(batchId, take, ct);
        var dtos = readings.Select(r => new SensorReadingDto(
            r.Id, r.BatchId, r.DeviceId, r.TemperatureC, r.HumidityPercent, r.Latitude, r.Longitude, r.RecordedAtUtc)
        ).ToList();

        return Success(dtos);
    }
}
