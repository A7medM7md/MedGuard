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
    DateTime RecordedAtUtc,
    string? TraceParent = null); // W3C trace context from the device (ADR 0011); null = not traced

public enum IngestOutcome
{
    Recorded,
    /// <summary>This MessageId was already processed: a redelivery, safely ignored.</summary>
    Duplicate,
    /// <summary>The batch doesn't exist. No retry will fix that, so it's dead-lettered.</summary>
    BatchNotFound,
    /// <summary>The domain refused it (e.g. a critical breach on a Delivered batch). Only
    /// returned by the batch path; the single path throws the DomainException instead.</summary>
    Rejected,
}

public record IngestResult(IngestOutcome Outcome, AlertSeverity? RaisedAlert = null, string? AlertMessage = null, string? Error = null)
{
    public static readonly IngestResult Duplicate = new(IngestOutcome.Duplicate);
    public static readonly IngestResult BatchNotFound = new(IngestOutcome.BatchNotFound);
    public static IngestResult Rejected(string error) => new(IngestOutcome.Rejected, Error: error);
}
