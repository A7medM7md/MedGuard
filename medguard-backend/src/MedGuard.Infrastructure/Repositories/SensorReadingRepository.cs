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

        // Translates to ROW_NUMBER() OVER (PARTITION BY BatchId ORDER BY RecordedAtUtc DESC),
        // served by the (BatchId, RecordedAtUtc) index.
        public async Task<IReadOnlyList<SensorReading>> GetLatestPerBatchAsync(CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .GroupBy(r => r.BatchId)
                .Select(g => g.OrderByDescending(r => r.RecordedAtUtc).First())
                .ToListAsync(ct);

        public Task<bool> ExistsByMessageIdAsync(Guid messageId, CancellationToken ct = default) =>
            Set.AnyAsync(r => r.MessageId == messageId, ct);

        // EF8 sends the id list as ONE JSON parameter (OPENJSON), not N parameters,
        // so the SQL text (and its cached plan) is the same whatever the batch size.
        public async Task<IReadOnlyList<Guid>> GetExistingMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default) =>
            await Set.AsNoTracking()
                .Where(r => r.MessageId != null && messageIds.Contains(r.MessageId.Value))
                .Select(r => r.MessageId!.Value)
                .ToListAsync(ct);
    }
}
