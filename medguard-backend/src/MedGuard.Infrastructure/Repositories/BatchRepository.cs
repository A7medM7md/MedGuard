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

    public async Task<IReadOnlyList<Batch>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await Set.Where(b => ids.Contains(b.Id)).ToListAsync(ct);

    // Filtered includes: the newest N rows are picked by SQL Server. Including every
    // reading and trimming in memory made this call slower with each reading a batch
    // ever recorded, and the Monitoring page calls it for every batch every 10s.
    // Split queries: one JOIN across three collections would multiply their rows.
    public async Task<Batch?> GetWithHistoryAsync(Guid batchId, int readings, int alerts, CancellationToken ct = default) =>
        await Set
            .AsNoTracking()
            .AsSplitQuery()
            .Include(b => b.SensorReadings.OrderByDescending(r => r.RecordedAtUtc).Take(readings))
            .Include(b => b.Alerts.OrderByDescending(a => a.TriggeredAtUtc).Take(alerts))
            .Include(b => b.Shipments)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
}
