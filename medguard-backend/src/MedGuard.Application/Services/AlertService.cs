using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class AlertService : IAlertService
{
    private readonly IUnitOfWork _uow;
    public AlertService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<AlertDto>> GetUnresolvedAsync(CancellationToken ct = default)
    {
        var alerts = await _uow.Alerts.GetUnresolvedAsync(ct);
        return alerts.Select(a => new AlertDto(a.Id, a.BatchId, a.Severity, a.Message, a.TriggeredAtUtc, a.IsResolved, a.ResolvedAtUtc)).ToList();
    }

    public async Task<PagedResult<AlertDto>> GetPagedAsync(
        int page,
        int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _uow.Alerts.GetPagedAsync(page, pageSize, severity, isResolved, ct);
        var dtos = items.Select(a => new AlertDto(a.Id, a.BatchId, a.Severity, a.Message, a.TriggeredAtUtc, a.IsResolved, a.ResolvedAtUtc)).ToList();

        return new PagedResult<AlertDto>(dtos, page, pageSize, totalCount);
    }

    public async Task ResolveAsync(Guid alertId, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(alertId, ct)
            ?? throw new NotFoundException(nameof(Alert), alertId);

        // Resolve() is idempotent and owns its own timestamping — no manual field setting.
        alert.Resolve();
        _uow.Alerts.Update(alert);
        await _uow.SaveChangesAsync(ct);
    }
}