using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;

namespace MedGuard.Domain.Entities;

public class Device : BaseEntity
{
    // Default "stale" threshold when no org setting is supplied — kept only as a
    // fallback for callers that don't pass one (e.g. domain unit tests).
    private static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(5);

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
    /// <param name="staleAfter">
    /// Settings > Alerting > Device silent after. "Offline" is 6x this threshold —
    /// there's no separate setting for it, so it stays proportional to whatever the
    /// org configures for "stale".
    /// </param>
    public DeviceStatus GetStatus(DateTime? nowUtc = null, TimeSpan? staleAfter = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var stale = staleAfter ?? DefaultStaleAfter;
        var offline = stale * 6;
        var elapsed = now - LastSeenAtUtc;

        if (elapsed <= stale) return DeviceStatus.Online;
        if (elapsed <= offline) return DeviceStatus.Stale;
        return DeviceStatus.Offline;
    }
}