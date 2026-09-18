using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class AlertService : ResponseHandler, IAlertService
{
    private readonly IUnitOfWork _uow;
    public AlertService(IUnitOfWork uow) => _uow = uow;

    public async Task<Response<List<AlertDto>>> GetUnresolvedAsync(CancellationToken ct = default)
    {
        var alerts = await _uow.Alerts.GetUnresolvedAsync(ct);
        var dtos = alerts.Select(ToDto).ToList();
        return Success(dtos);
    }

    public async Task<Response<PagedResult<AlertDto>>> GetPagedAsync(int page, int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _uow.Alerts.GetPagedAsync(page, pageSize, severity, isResolved, ct);
        var dtos = items.Select(ToDto).ToList();

        var result = new PagedResult<AlertDto>(dtos, page, pageSize, totalCount);

        return Success(result);
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

    private static AlertDto ToDto(Alert a) => new(
        a.Id, a.BatchId, a.Batch?.BatchNumber ?? string.Empty, a.Batch?.DrugName ?? string.Empty,
        a.Severity, a.Message, a.TriggeredAtUtc, a.IsResolved, a.ResolvedAtUtc);
}