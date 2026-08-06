namespace MedGuard.Domain.Enums;

public enum ShipmentStatus
{
    Preparing = 0,
    InTransit = 1,
    Delivered = 2,
    Aborted = 3   // Aborted mid-route due to a critical cold-chain breach
}
