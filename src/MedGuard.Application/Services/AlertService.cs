using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
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

    public async Task ResolveAsync(Guid alertId, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(alertId, ct)
            ?? throw new NotFoundException(nameof(Alert), alertId);

        alert.Resolve();
        _uow.Alerts.Update(alert);
        await _uow.SaveChangesAsync(ct);
    }
}
