using MedGuard.Domain.Common;

namespace MedGuard.Domain.Entities;

/// <summary>
/// A single telemetry reading pushed from an IoT sensor attached to a batch's
/// storage unit or delivery vehicle. Always created through Batch.RecordReading —
/// never inserted directly, so it can never exist without being evaluated.
/// </summary>
public class SensorReading : BaseEntity
{
    public Guid BatchId { get; private set; }
    public Batch? Batch { get; private set; }

    public string DeviceId { get; private set; } = default!;
    public decimal TemperatureC { get; private set; }
    public decimal? HumidityPercent { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    private SensorReading() { }

    public SensorReading(
        Guid batchId,
        string deviceId,
        decimal temperatureC,
        decimal? humidityPercent = null,
        double? latitude = null,
        double? longitude = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device id is required.", nameof(deviceId));

        BatchId = batchId;
        DeviceId = deviceId;
        TemperatureC = temperatureC;
        HumidityPercent = humidityPercent;
        Latitude = latitude;
        Longitude = longitude;
        RecordedAtUtc = DateTime.UtcNow;
    }
}
