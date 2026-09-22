using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IShipmentRepository : IRepository<Shipment>
{
    Task<IReadOnlyList<Shipment>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>All shipments with their Batch navigation eagerly loaded — backs the Shipments list page's BatchNumber column.</summary>
    Task<IReadOnlyList<Shipment>> GetAllWithBatchAsync(CancellationToken ct = default);
}
