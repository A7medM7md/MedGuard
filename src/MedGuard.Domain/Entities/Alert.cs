using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;

namespace MedGuard.Domain.Entities;

/// <summary>
/// Raised when a sensor reading breaches (or approaches) a batch's safe temperature
/// range. Only ever created via the internal factory below, which Batch.RecordReading
/// calls — there's no public constructor, so an Alert can't exist detached from the
/// reading that triggered it.
/// </summary>
public class Alert : BaseEntity
{
    public Guid BatchId { get; private set; }
    public Batch? Batch { get; private set; }

    public Guid SensorReadingId { get; private set; }
    public SensorReading SensorReading { get; private set; } = default!;

    public AlertSeverity Severity { get; private set; }
    public string Message { get; private set; } = default!;
    public DateTime TriggeredAtUtc { get; private set; }
    public bool IsResolved { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    private Alert() { }

    private Alert(Guid batchId, Guid sensorReadingId, AlertSeverity severity, string message)
    {
        BatchId = batchId;
        SensorReadingId = sensorReadingId;
        Severity = severity;
        Message = message;
        TriggeredAtUtc = DateTime.UtcNow;
    }

    internal static Alert Raise(Guid batchId, Guid sensorReadingId, AlertSeverity severity, string message) =>
        new(batchId, sensorReadingId, severity, message);

    public void Resolve()
    {
        if (IsResolved) return;
        IsResolved = true;
        ResolvedAtUtc = DateTime.UtcNow;
    }
}
