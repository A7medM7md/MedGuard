using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;

namespace MedGuard.Domain.Interfaces;

public interface IAlertRepository : IRepository<Alert>
{
    Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Alert>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);

    Task<(IReadOnlyList<Alert> Items, int TotalCount)> GetPagedAsync(int page, int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default);
}