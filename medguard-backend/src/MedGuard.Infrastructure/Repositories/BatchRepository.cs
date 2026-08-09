using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories;

public class BatchRepository : EfRepository<Batch>, IBatchRepository
{
    public BatchRepository(MedGuardDbContext context) : base(context) { }

    public async Task<Batch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(b => b.BatchNumber == batchNumber, ct);

    public async Task<IReadOnlyList<Batch>> GetByStatusAsync(BatchStatus status, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(b => b.Status == status).ToListAsync(ct);

    public async Task<Batch?> GetWithHistoryAsync(Guid batchId, CancellationToken ct = default) =>
        await Set
            .Include(b => b.SensorReadings)
            .Include(b => b.Alerts)
            .Include(b => b.Shipments)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
}
