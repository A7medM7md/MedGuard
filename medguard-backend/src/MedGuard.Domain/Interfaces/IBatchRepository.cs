using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IBatchRepository : IRepository<Batch>
{
    Task<Batch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Batch>> GetByStatusAsync(Enums.BatchStatus status, CancellationToken ct = default);

    /// <summary>Loads a batch with its readings, alerts and shipments in one query — used for the batch detail view.</summary>
    Task<Batch?> GetWithHistoryAsync(Guid batchId, CancellationToken ct = default);
}
