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

        // All breach-evaluation, alert-raising and auto-quarantine logic lives inside the
        // aggregate now — this service just hands the reading to the batch and persists.
        batch.RecordReading(reading);

        await _uow.SensorReadings.AddAsync(reading, ct);
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
