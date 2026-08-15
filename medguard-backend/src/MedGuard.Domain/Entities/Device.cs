using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;

namespace MedGuard.Domain.Entities;

public class Device : BaseEntity
{
    // Device is "stale" 5-30 min after its last heartbeat, "offline" beyond that —
    // matches the frontend's device-silence thresholds.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(30);

    public string DeviceCode { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public Guid? AssignedBatchId { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public int BatteryPercent { get; private set; }
    public int SignalPercent { get; private set; }

    private Device() { }

    public Device(string deviceCode, string model)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
            throw new ArgumentException("Device code is required.", nameof(deviceCode));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Device model is required.", nameof(model));

        DeviceCode = deviceCode;
        Model = model;
        LastSeenAtUtc = DateTime.UtcNow;
        BatteryPercent = 100;
        SignalPercent = 100;
    }

    public void RecordHeartbeat(int batteryPercent, int signalPercent)
    {
        if (batteryPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(batteryPercent), "Battery percent must be 0-100.");
        if (signalPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(signalPercent), "Signal percent must be 0-100.");

        LastSeenAtUtc = DateTime.UtcNow;
        BatteryPercent = batteryPercent;
        SignalPercent = signalPercent;
    }

    public void AssignToBatch(Guid batchId)
    {
        if (AssignedBatchId is not null)
            throw new InvalidDeviceAssignmentException(DeviceCode, "is already assigned to a batch");
        AssignedBatchId = batchId;
    }

    public void Unassign() => AssignedBatchId = null;

    /// <summary>Status is derived, never stored — a device can't silently go "stale" in the
    /// database without a background job ticking it; computing it from LastSeenAtUtc means
    /// it's always correct the instant it's read.</summary>
    public DeviceStatus GetStatus(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var elapsed = now - LastSeenAtUtc;

        if (elapsed <= StaleAfter) return DeviceStatus.Online;
        if (elapsed <= OfflineAfter) return DeviceStatus.Stale;
        return DeviceStatus.Offline;
    }
}