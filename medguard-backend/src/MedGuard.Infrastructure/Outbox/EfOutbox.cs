using System.Text.Json;
using MedGuard.Application.Interfaces;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Tracing;
using MedGuard.Infrastructure.Persistence;

namespace MedGuard.Infrastructure.Outbox;

/// <summary>
/// Adds the event to the SAME scoped DbContext the repositories use. Nothing is
/// written until the unit of work calls SaveChanges, and then it's written inside
/// that SaveChanges' transaction, which is the whole point.
/// </summary>
public sealed class EfOutbox : IOutbox
{
    private readonly MedGuardDbContext _db;

    public EfOutbox(MedGuardDbContext db) => _db = db;

    public void Enqueue<T>(string topic, string key, T payload) where T : notnull =>
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Topic = topic,
            MessageKey = key,
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(payload, ContractJson.Options),
            TraceParent = TraceContext.Current(),
        });
}
