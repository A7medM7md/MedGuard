using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;

namespace MedGuard.Domain.Entities;

/// <summary>
/// A single leg of a batch's journey (e.g. Factory -> Regional Warehouse -> Pharmacy).
/// Only ever created via Batch.Ship() — the constructor is internal so a shipment can
/// never exist without an owning batch that has approved it (i.e. isn't quarantined).
/// </summary>
public class Shipment : BaseEntity
{
    public Guid BatchId { get; private set; }
    public Batch? Batch { get; private set; }

    public string OriginLocation { get; private set; } = default!;
    public string DestinationLocation { get; private set; } = default!;
    public string? CourierName { get; private set; }
    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Preparing;
    public DateTime? DepartedAtUtc { get; private set; }
    public DateTime? ArrivedAtUtc { get; private set; }

    private Shipment() { }

    internal Shipment(Guid batchId, string originLocation, string destinationLocation, string? courierName)
    {
        if (string.IsNullOrWhiteSpace(originLocation))
            throw new ArgumentException("Origin location is required.", nameof(originLocation));
        if (string.IsNullOrWhiteSpace(destinationLocation))
            throw new ArgumentException("Destination location is required.", nameof(destinationLocation));

        BatchId = batchId;
        OriginLocation = originLocation;
        DestinationLocation = destinationLocation;
        CourierName = courierName;
        Status = ShipmentStatus.Preparing;
    }

    internal void Depart()
    {
        if (Status != ShipmentStatus.Preparing)
            throw new InvalidShipmentStateTransitionException(Id, Status.ToString(), "depart");
        Status = ShipmentStatus.InTransit;
        DepartedAtUtc = DateTime.UtcNow;
    }

    public void MarkDelivered()
    {
        if (Status != ShipmentStatus.InTransit)
            throw new InvalidShipmentStateTransitionException(Id, Status.ToString(), "be marked as delivered");
        Status = ShipmentStatus.Delivered;
        ArrivedAtUtc = DateTime.UtcNow;
    }

    public void Abort()
    {
        if (Status == ShipmentStatus.Delivered)
            throw new InvalidShipmentStateTransitionException(Id, Status.ToString(), "be aborted");
        Status = ShipmentStatus.Aborted;
    }
}
