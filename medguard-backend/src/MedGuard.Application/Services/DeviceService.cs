using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class DeviceService : ResponseHandler, IDeviceService
{
    private readonly IUnitOfWork _uow;
    public DeviceService(IUnitOfWork uow) => _uow = uow;

    public async Task<Response<DeviceDto>> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var existing = await _uow.Devices.GetByDeviceCodeAsync(request.DeviceCode, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Device code '{request.DeviceCode}' is already registered.");

        var device = new Device(request.DeviceCode, request.Model);
        await _uow.Devices.AddAsync(device, ct);
        await _uow.SaveChangesAsync(ct);

        var result = await ToDtoAsync(device, ct);
        return Success(result);
    }

    public async Task<Response<DeviceDto>> RecordHeartbeatAsync(Guid deviceId, DeviceHeartbeatRequest request, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw new NotFoundException(nameof(Device), deviceId);

        device.RecordHeartbeat(request.BatteryPercent, request.SignalPercent);
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);

        var result = await ToDtoAsync(device, ct);
        return Success(result);
    }

    public async Task<Response<DeviceDto>> AssignToBatchAsync(Guid deviceId, Guid batchId, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw new NotFoundException(nameof(Device), deviceId);

        // Confirms the batch actually exists before wiring the assignment —
        // AssignToBatch itself only guards "not already assigned".
        _ = await _uow.Batches.GetByIdAsync(batchId, ct)
            ?? throw new NotFoundException(nameof(Batch), batchId);

        device.AssignToBatch(batchId);
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);

        var result = await ToDtoAsync(device, ct);
        return Success(result);
    }

    public async Task<Response<DeviceDto>> UnassignAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw new NotFoundException(nameof(Device), deviceId);

        device.Unassign();
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);

        var result = await ToDtoAsync(device, ct);
        return Success(result);
    }

    public async Task<Response<List<DeviceDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var devices = await _uow.Devices.GetAllAsync(ct);

        if (devices is null)
            return NotFound<List<DeviceDto>>();

        // Device only carries a bare AssignedBatchId (no EF navigation to Batch), so batch
        // numbers are resolved with one bulk lookup here rather than a lookup per device.
        var batches = await _uow.Batches.GetAllAsync(ct);
        var batchNumbersById = batches.ToDictionary(b => b.Id, b => b.BatchNumber);

        var result = devices
            .Select(d => ToDto(d, d.AssignedBatchId.HasValue ? batchNumbersById.GetValueOrDefault(d.AssignedBatchId.Value) : null))
            .ToList();
        return Success(result);
    }

    private async Task<DeviceDto> ToDtoAsync(Device d, CancellationToken ct)
    {
        string? batchNumber = null;
        if (d.AssignedBatchId is { } batchId)
        {
            var batch = await _uow.Batches.GetByIdAsync(batchId, ct);
            batchNumber = batch?.BatchNumber;
        }
        return ToDto(d, batchNumber);
    }

    private static DeviceDto ToDto(Device d, string? assignedBatchNumber) => new(
        d.Id, d.DeviceCode, d.Model, d.AssignedBatchId, assignedBatchNumber, d.LastSeenAtUtc,
        d.BatteryPercent, d.SignalPercent, d.GetStatus());
}
