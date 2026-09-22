using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class ColdChainMonitoringService : ResponseHandler, IColdChainMonitoringService
{
    private readonly IUnitOfWork _uow;

    public ColdChainMonitoringService(IUnitOfWork uow) => _uow = uow;

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

        // Warning-margin and auto-quarantine are org-configurable (Settings page) —
        // read the live values rather than hardcoding them here.
        var settings = await _uow.OrgSettings.GetSingletonAsync(ct);
        var warningMarginPercent = settings?.WarningMarginPercent ?? 15m;
        var autoQuarantineOnBreach = settings?.AutoQuarantineOnBreach ?? true;

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
            await _uow.Alerts.AddAsync(alert, ct);
        await _uow.SaveChangesAsync(ct);

        var result = new SensorReadingDto(reading.Id, reading.BatchId, reading.DeviceId, reading.TemperatureC,
            reading.HumidityPercent, reading.Latitude, reading.Longitude, reading.RecordedAtUtc);

        return Success(result);
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
