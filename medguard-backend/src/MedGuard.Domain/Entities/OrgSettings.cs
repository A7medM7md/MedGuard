using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;

namespace MedGuard.Domain.Entities;

/// <summary>
/// Single-row, org-wide configuration (Settings page). Deliberately modeled as one
/// row rather than a table keyed by tenant — this system is single-tenant today;
/// see Roadmap's multi-tenant item for when that changes.
/// </summary>
public class OrgSettings : BaseEntity
{
    public string OrganizationName { get; private set; } = default!;
    public string TimeZone { get; private set; } = default!;

    /// <summary>How far outside the safe range (as a % of its width) is still a Warning, not Critical.</summary>
    public decimal WarningMarginPercent { get; private set; }
    public bool AutoQuarantineOnBreach { get; private set; }

    public AlertChannel CriticalAlertChannels { get; private set; }
    public AlertChannel WarningAlertChannels { get; private set; }
    public int DeviceSilentAfterMinutes { get; private set; }

    // EF Core materializes through this — never call it from app code.
    private OrgSettings() { }

    private OrgSettings(
        string organizationName,
        string timeZone,
        decimal warningMarginPercent,
        bool autoQuarantineOnBreach,
        AlertChannel criticalAlertChannels,
        AlertChannel warningAlertChannels,
        int deviceSilentAfterMinutes)
    {
        OrganizationName = organizationName;
        TimeZone = timeZone;
        WarningMarginPercent = warningMarginPercent;
        AutoQuarantineOnBreach = autoQuarantineOnBreach;
        CriticalAlertChannels = criticalAlertChannels;
        WarningAlertChannels = warningAlertChannels;
        DeviceSilentAfterMinutes = deviceSilentAfterMinutes;
    }

    /// <summary>Sane defaults for the one row this system needs the first time it's read.</summary>
    public static OrgSettings CreateDefault() => new(
        organizationName: "MedGuard Operations",
        timeZone: "UTC",
        warningMarginPercent: 15m,
        autoQuarantineOnBreach: true,
        criticalAlertChannels: AlertChannel.Email | AlertChannel.Sms | AlertChannel.InApp,
        warningAlertChannels: AlertChannel.InApp,
        deviceSilentAfterMinutes: 5);

    public void Update(
        string organizationName,
        string timeZone,
        decimal warningMarginPercent,
        bool autoQuarantineOnBreach,
        AlertChannel criticalAlertChannels,
        AlertChannel warningAlertChannels,
        int deviceSilentAfterMinutes)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
            throw new ArgumentException("Organization name is required.", nameof(organizationName));
        if (string.IsNullOrWhiteSpace(timeZone))
            throw new ArgumentException("Time zone is required.", nameof(timeZone));
        if (warningMarginPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(warningMarginPercent), "Warning margin must be 0-100%.");
        if (deviceSilentAfterMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceSilentAfterMinutes), "Must be positive.");

        OrganizationName = organizationName;
        TimeZone = timeZone;
        WarningMarginPercent = warningMarginPercent;
        AutoQuarantineOnBreach = autoQuarantineOnBreach;
        CriticalAlertChannels = criticalAlertChannels;
        WarningAlertChannels = warningAlertChannels;
        DeviceSilentAfterMinutes = deviceSilentAfterMinutes;
    }
}
