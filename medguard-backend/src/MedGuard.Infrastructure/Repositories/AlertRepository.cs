using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories;

public class AlertRepository : EfRepository<Alert>, IAlertRepository
{
    public AlertRepository(MedGuardDbContext context) : base(context) { }

    public async Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Include(a => a.Batch)
            .Where(a => !a.IsResolved)
            .OrderByDescending(a => a.TriggeredAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Alert>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Include(a => a.Batch)
            .Where(a => a.BatchId == batchId)
            .OrderByDescending(a => a.TriggeredAtUtc)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Alert> Items, int TotalCount)> GetPagedAsync(int page, int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Include(a => a.Batch).AsQueryable();

        if (severity.HasValue)
            query = query.Where(a => a.Severity == severity.Value);
        if (isResolved.HasValue)
            query = query.Where(a => a.IsResolved == isResolved.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.TriggeredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
