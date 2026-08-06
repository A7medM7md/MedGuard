using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IShipmentRepository : IRepository<Shipment>
{
    Task<IReadOnlyList<Shipment>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);
}
