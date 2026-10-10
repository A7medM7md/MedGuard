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

    /// <summary>
    /// The device-generated id of the message this reading arrived in (null for
    /// readings posted over HTTP). Unique: the pipeline is at-least-once, so the
    /// same message can be delivered twice, and this is what makes the second
    /// delivery a no-op instead of a duplicate reading and a duplicate alert.
    /// </summary>
    public Guid? MessageId { get; private set; }

    private SensorReading() { }

    public SensorReading(
        Guid batchId,
        string deviceId,
        decimal temperatureC,
        decimal? humidityPercent = null,
        double? latitude = null,
        double? longitude = null,
        DateTime? recordedAtUtc = null,
        Guid? messageId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device id is required.", nameof(deviceId));

        BatchId = batchId;
        DeviceId = deviceId;
        TemperatureC = temperatureC;
        HumidityPercent = humidityPercent;
        Latitude = latitude;
        Longitude = longitude;
        // Readings that come through Kafka carry the time the SENSOR took them,
        // which can be minutes or hours old if the device was offline and is
        // flushing a backlog. HTTP readings are stamped on arrival.
        RecordedAtUtc = recordedAtUtc ?? DateTime.UtcNow;
        MessageId = messageId;
    }
}
