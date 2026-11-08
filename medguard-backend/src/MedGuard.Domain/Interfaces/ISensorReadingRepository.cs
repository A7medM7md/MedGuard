using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface ISensorReadingRepository : IRepository<SensorReading>
{
    Task<IReadOnlyList<SensorReading>> GetByBatchIdAsync(Guid batchId, int take = 100, CancellationToken ct = default);
    Task<SensorReading?> GetLatestForBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<bool> ExistsByMessageIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Which of these message ids are already recorded. One query for a whole consumer batch.</summary>
    Task<IReadOnlyList<Guid>> GetExistingMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
}
