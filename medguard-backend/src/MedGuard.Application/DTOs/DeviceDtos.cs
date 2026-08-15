using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record RegisterDeviceRequest(string DeviceCode, string Model);

public record DeviceHeartbeatRequest(int BatteryPercent, int SignalPercent);

public record DeviceDto(Guid Id, string DeviceCode,
    string Model,
    Guid? AssignedBatchId,
    DateTime LastSeenAtUtc,
    int BatteryPercent,
    int SignalPercent,
    DeviceStatus Status
);
