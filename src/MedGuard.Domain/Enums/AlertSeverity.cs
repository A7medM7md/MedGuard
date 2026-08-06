namespace MedGuard.Domain.Enums;

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,     // Approaching threshold
    Critical = 2      // Threshold breached — batch must be quarantined
}
