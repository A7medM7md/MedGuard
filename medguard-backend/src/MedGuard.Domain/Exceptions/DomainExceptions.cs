namespace MedGuard.Domain.Exceptions;

/// <summary>Base type for every exception raised by a broken domain invariant.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class InvalidTemperatureRangeException : DomainException
{
    public InvalidTemperatureRangeException(decimal min, decimal max)
        : base($"Invalid safe temperature range: min ({min}C) must be less than max ({max}C).") { }
}

public sealed class InvalidBatchDatesException : DomainException
{
    public InvalidBatchDatesException(DateTime manufacturedUtc, DateTime expiryUtc)
        : base($"Expiry date ({expiryUtc:d}) must be after the manufactured date ({manufacturedUtc:d}).") { }
}

public sealed class InvalidBatchStateTransitionException : DomainException
{
    public InvalidBatchStateTransitionException(string batchNumber, string currentStatus, string attemptedAction)
        : base($"Batch {batchNumber} cannot {attemptedAction} while its status is '{currentStatus}'.") { }
}

public sealed class InvalidShipmentStateTransitionException : DomainException
{
    public InvalidShipmentStateTransitionException(Guid shipmentId, string currentStatus, string attemptedAction)
        : base($"Shipment {shipmentId} cannot {attemptedAction} while its status is '{currentStatus}'.") { }
}

public sealed class InvalidDeviceAssignmentException : DomainException
{
    public InvalidDeviceAssignmentException(string deviceCode, string reason)
        : base($"Device {deviceCode} {reason}.") { }
}