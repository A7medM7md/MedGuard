namespace MedGuard.Contracts.Events;

/// <summary>
/// Published to <c>batch-alerts</c> (key = BatchId) whenever a reading raises an
/// alert. It's a wire contract like <c>DeviceTelemetry</c>: change it additively only.
///
/// It carries everything a UI needs to render the alert (batch number, drug,
/// new batch status), so a subscriber never has to call back into the API to
/// make sense of it. That's "event-carried state transfer": the event is
/// self-contained, and the producer doesn't get hit by N follow-up queries.
/// </summary>
/// <param name="Severity">"Warning" | "Critical". A string, not the enum's int,
/// so reordering the C# enum can never silently change the meaning on the wire.</param>
/// <param name="BatchStatus">The batch's status AFTER this reading, e.g. "Quarantined"
/// when the reading triggered auto-quarantine.</param>
public record BatchAlertRaised(
    Guid AlertId,
    Guid BatchId,
    string BatchNumber,
    string DrugName,
    string Severity,
    string Message,
    decimal TemperatureC,
    string BatchStatus,
    string DeviceCode,
    DateTime TriggeredAtUtc);
