namespace MedGuard.Domain.Enums;

public enum BatchStatus
{
    Active = 0,
    InTransit = 1,
    Delivered = 2,
    Quarantined = 3,   // Temperature breach detected — pulled from distribution
    Recalled = 4        // Confirmed unsafe — must not reach patients
}
