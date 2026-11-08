namespace MedGuard.Application.Interfaces;

/// <summary>
/// "Publish this event, but only if my database changes commit."
///
/// <see cref="Enqueue{T}"/> does NOT talk to Kafka. It stages the event in the
/// same unit of work as the entities, so the next <c>IUnitOfWork.SaveChangesAsync</c>
/// writes the business rows AND the event row in one transaction: both or neither.
/// A separate dispatcher publishes committed events to Kafka afterwards (ADR 0006).
/// </summary>
public interface IOutbox
{
    void Enqueue<T>(string topic, string key, T payload) where T : notnull;
}
