using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class DeviceService : IDeviceService
{
    private readonly IUnitOfWork _uow;
    public DeviceService(IUnitOfWork uow) => _uow = uow;

    public async Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var existing = await _uow.Devices.GetByDeviceCodeAsync(request.DeviceCode, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Device code '{request.DeviceCode}' is already registered.");

        var device = new Device(request.DeviceCode, request.Model);
        await _uow.Devices.AddAsync(device, ct);
        await _uow.SaveChangesAsync(ct);

        return ToDto(device);
    }

    public async Task<DeviceDto> RecordHeartbeatAsync(Guid deviceId, DeviceHeartbeatRequest request, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw new NotFoundException(nameof(Device), deviceId);

        device.RecordHeartbeat(request.BatteryPercent, request.SignalPercent);
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);

        return ToDto(device);
    }

    public async Task<DeviceDto> AssignToBatchAsync(Guid deviceId, Guid batchId, CancellationToken ct = default)
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

        return ToDto(device);
    }

    public async Task<DeviceDto> UnassignAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
            ?? throw new NotFoundException(nameof(Device), deviceId);

        device.Unassign();
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);

        return ToDto(device);
    }

    public async Task<IReadOnlyList<DeviceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var devices = await _uow.Devices.GetAllAsync(ct);
        return devices.Select(ToDto).ToList();
    }

    private static DeviceDto ToDto(Device d) => new(
        d.Id, d.DeviceCode, d.Model, d.AssignedBatchId, d.LastSeenAtUtc,
        d.BatteryPercent, d.SignalPercent, d.GetStatus());
}
