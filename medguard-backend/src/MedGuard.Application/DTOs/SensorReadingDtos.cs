namespace MedGuard.Application.DTOs;

/// <summary>What an IoT device (or its gateway) posts every time it records a reading.</summary>
public record RecordReadingRequest(
    Guid BatchId,
    string DeviceId,
    decimal TemperatureC,
    decimal? HumidityPercent,
    double? Latitude,
    double? Longitude
);

public record SensorReadingDto(
    Guid Id,
    Guid BatchId,
    string DeviceId,
    decimal TemperatureC,
    decimal? HumidityPercent,
    double? Latitude,
    double? Longitude,
    DateTime RecordedAtUtc
);
