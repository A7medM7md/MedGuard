using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories
{
    public class SensorReadingRepository : EfRepository<SensorReading>, ISensorReadingRepository
    {
        public SensorReadingRepository(MedGuardDbContext context) : base(context) { }

        public async Task<IReadOnlyList<SensorReading>> GetByBatchIdAsync(Guid batchId, int take = 100, CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .Where(r => r.BatchId == batchId)
                .OrderByDescending(r => r.RecordedAtUtc)
                .Take(take)
                .ToListAsync(ct);

        public async Task<SensorReading?> GetLatestForBatchAsync(Guid batchId, CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .Where(r => r.BatchId == batchId)
                .OrderByDescending(r => r.RecordedAtUtc)
                .FirstOrDefaultAsync(ct);
    }
}
