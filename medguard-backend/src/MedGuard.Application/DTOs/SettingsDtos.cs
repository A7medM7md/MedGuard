using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record SettingsDto(
    string OrganizationName,
    string TimeZone,
    decimal WarningMarginPercent,
    bool AutoQuarantineOnBreach,
    AlertChannel CriticalAlertChannels,
    AlertChannel WarningAlertChannels,
    int DeviceSilentAfterMinutes
);

public record UpdateSettingsRequest(
    string OrganizationName,
    string TimeZone,
    decimal WarningMarginPercent,
    bool AutoQuarantineOnBreach,
    AlertChannel CriticalAlertChannels,
    AlertChannel WarningAlertChannels,
    int DeviceSilentAfterMinutes
);
