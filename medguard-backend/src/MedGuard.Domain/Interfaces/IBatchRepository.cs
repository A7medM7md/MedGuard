using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IBatchRepository : IRepository<Batch>
{
    Task<Batch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Batch>> GetByStatusAsync(Enums.BatchStatus status, CancellationToken ct = default);

    /// <summary>Loads several batches, TRACKED (they're about to be modified), in one query.</summary>
    Task<IReadOnlyList<Batch>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Loads a batch, read-only, with its newest <paramref name="readings"/> readings and
    /// <paramref name="alerts"/> alerts plus all its shipments — used for the batch detail view.
    /// </summary>
    Task<Batch?> GetWithHistoryAsync(Guid batchId, int readings, int alerts, CancellationToken ct = default);
}
