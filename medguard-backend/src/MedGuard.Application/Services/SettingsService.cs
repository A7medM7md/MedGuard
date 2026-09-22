using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class SettingsService : ResponseHandler, ISettingsService
{
    private readonly IUnitOfWork _uow;
    public SettingsService(IUnitOfWork uow) => _uow = uow;

    public async Task<Response<SettingsDto>> GetAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSingletonAsync(ct);
        return Success(ToDto(settings));
    }

    public async Task<Response<SettingsDto>> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSingletonAsync(ct);

        settings.Update(
            request.OrganizationName,
            request.TimeZone,
            request.WarningMarginPercent,
            request.AutoQuarantineOnBreach,
            request.CriticalAlertChannels,
            request.WarningAlertChannels,
            request.DeviceSilentAfterMinutes);

        _uow.OrgSettings.Update(settings);
        await _uow.SaveChangesAsync(ct);

        return Success(ToDto(settings), "Settings updated.");
    }

    // The whole app reads/writes exactly one settings row — created lazily on first
    // access instead of via a data seed, so a fresh database needs no manual step.
    private async Task<OrgSettings> GetOrCreateSingletonAsync(CancellationToken ct)
    {
        var settings = await _uow.OrgSettings.GetSingletonAsync(ct);
        if (settings is not null)
            return settings;

        settings = OrgSettings.CreateDefault();
        await _uow.OrgSettings.AddAsync(settings, ct);
        await _uow.SaveChangesAsync(ct);
        return settings;
    }

    private static SettingsDto ToDto(OrgSettings s) => new(
        s.OrganizationName, s.TimeZone, s.WarningMarginPercent, s.AutoQuarantineOnBreach,
        s.CriticalAlertChannels, s.WarningAlertChannels, s.DeviceSilentAfterMinutes);
}
