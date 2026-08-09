using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories;

public class ShipmentRepository : EfRepository<Shipment>, IShipmentRepository
{
    public ShipmentRepository(MedGuardDbContext context) : base(context) { }

    public async Task<IReadOnlyList<Shipment>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(s => s.BatchId == batchId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
}
