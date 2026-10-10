using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

/// <summary>A reading as it arrives from the event pipeline (Kafka), not from HTTP.</summary>
public record IngestReadingCommand(
    Guid MessageId,
    Guid BatchId,
    string DeviceCode,
    decimal TemperatureC,
    decimal? HumidityPercent,
    double? Latitude,
    double? Longitude,
    DateTime RecordedAtUtc);

public enum IngestOutcome
{
    Recorded,
    /// <summary>This MessageId was already processed: a redelivery, safely ignored.</summary>
    Duplicate,
    /// <summary>The batch doesn't exist. No retry will fix that, so it's dead-lettered.</summary>
    BatchNotFound,
}

public record IngestResult(IngestOutcome Outcome, AlertSeverity? RaisedAlert = null, string? AlertMessage = null)
{
    public static readonly IngestResult Duplicate = new(IngestOutcome.Duplicate);
    public static readonly IngestResult BatchNotFound = new(IngestOutcome.BatchNotFound);
}
