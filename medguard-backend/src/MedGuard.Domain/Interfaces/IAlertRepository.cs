using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IAlertRepository : IRepository<Alert>
{
    Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Alert>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);
}
