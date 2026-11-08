namespace MedGuard.Infrastructure.Outbox;

/// <summary>
/// An event waiting to be published (ADR 0006). Deliberately NOT a domain entity:
/// it's plumbing for reliable messaging, and the domain shouldn't know Kafka exists.
/// </summary>
public class OutboxMessage
{
    /// <summary>Also sent as the Kafka <c>message-id</c> header. The dispatcher is
    /// at-least-once, so consumers dedupe on it.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Topic { get; set; } = default!;
    public string MessageKey { get; set; } = default!;

    /// <summary>The contract's type name (e.g. <c>BatchAlertRaised</c>), sent as a header so a
    /// consumer of a topic carrying several event types knows what to deserialize into.</summary>
    public string Type { get; set; } = default!;

    /// <summary>The event, serialized to JSON when it's enqueued.</summary>
    public string Payload { get; set; } = default!;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null = still to be published. Set once Kafka acks the message.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
