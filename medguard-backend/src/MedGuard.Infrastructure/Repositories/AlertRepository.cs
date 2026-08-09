using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories
{
    public class AlertRepository : EfRepository<Alert>, IAlertRepository
    {
        public AlertRepository(MedGuardDbContext context) : base(context) { }

        public async Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .Where(a => !a.IsResolved)
                .OrderByDescending(a => a.TriggeredAtUtc)
                .ToListAsync(ct);

        public async Task<IReadOnlyList<Alert>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .Where(a => a.BatchId == batchId)
                .OrderByDescending(a => a.TriggeredAtUtc)
                .ToListAsync(ct);
    }
}
