using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Domain.ValueObjects;

namespace MedGuard.Application.Services;

public class BatchService : ResponseHandler, IBatchService
{
    private readonly IUnitOfWork _uow;

    public BatchService(IUnitOfWork uow) => _uow = uow;

    public async Task<Response<BatchDto>> CreateBatchAsync(CreateBatchRequest request, CancellationToken ct = default)
    {
        var existing = await _uow.Batches.GetByBatchNumberAsync(request.BatchNumber, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Batch number '{request.BatchNumber}' already exists.");

        // TemperatureRange validates Min < Max itself and throws InvalidTemperatureRangeException.
        var safeRange = new TemperatureRange(request.MinSafeTemperatureC, request.MaxSafeTemperatureC);

        // Batch's constructor validates dates/quantity and throws domain exceptions on its own —
        // no manual validation needed here, the aggregate protects itself.
        var batch = new Batch(
            request.BatchNumber,
            request.DrugName,
            request.ManufacturerName,
            request.QuantityUnits,
            request.ManufacturedDateUtc,
            request.ExpiryDateUtc,
            safeRange);

        await _uow.Batches.AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);

        var result = ToDto(batch);

        return Created(result, "Batch created successfully.");
    }

    public async Task<Response<BatchDto?>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _uow.Batches.GetByIdAsync(id, ct);

        if (batch is null)
            return NotFound<BatchDto?>($"Batch with ID '{id}' not found.");

        var result = ToDto(batch);

        return Success<BatchDto?>(result, "Batch retrieved successfully.");
    }

    public async Task<Response<BatchDetailDto>> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _uow.Batches.GetWithHistoryAsync(id, ct)
            ?? throw new NotFoundException(nameof(Batch), id);

        var readings = batch.SensorReadings
            .OrderByDescending(r => r.RecordedAtUtc)
            .Take(50)
            .Select(r => new SensorReadingDto(r.Id, r.BatchId, r.DeviceId, r.TemperatureC, r.HumidityPercent, r.Latitude, r.Longitude, r.RecordedAtUtc))
            .ToList();

        var alerts = batch.Alerts
            .OrderByDescending(a => a.TriggeredAtUtc)
            .Select(a => new AlertDto(a.Id, a.BatchId, batch.BatchNumber, batch.DrugName, a.Severity, a.Message, a.TriggeredAtUtc, a.IsResolved, a.ResolvedAtUtc))
            .ToList();

        var shipments = batch.Shipments
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new ShipmentDto(s.Id, s.BatchId, s.OriginLocation, s.DestinationLocation, s.CourierName, s.Status, s.DepartedAtUtc, s.ArrivedAtUtc))
            .ToList();

        var result = new BatchDetailDto(ToDto(batch), readings, alerts, shipments);

        return Success(result, "Batch details retrieved successfully.");
    }

    public async Task<Response<List<BatchDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var batches = await _uow.Batches.GetAllAsync(ct);
        var result = batches.Select(ToDto).ToList();
        return Success(result, "Batches retrieved successfully.");
    }

    // SafeRange.MinC / SafeRange.MaxC replace the old flat MinSafeTemperatureC / MaxSafeTemperatureC
    // properties now that temperature bounds are a TemperatureRange value object on the entity.
    private static BatchDto ToDto(Batch b) => new(
        b.Id, b.BatchNumber, b.DrugName, b.ManufacturerName, b.QuantityUnits,
        b.ManufacturedDateUtc, b.ExpiryDateUtc, b.SafeRange.MinC, b.SafeRange.MaxC, b.Status);
}