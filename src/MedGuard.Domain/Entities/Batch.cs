using MedGuard.Domain.Common;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;
using MedGuard.Domain.ValueObjects;

namespace MedGuard.Domain.Entities;

/// <summary>
/// Aggregate root: a manufactured batch of a temperature-sensitive drug, tracked from
/// the factory to the pharmacy. Owns SensorReadings, Alerts and Shipments — nothing
/// outside this class is allowed to mutate them directly, which is what keeps the
/// cold-chain invariant ("a critical breach always quarantines the batch") impossible
/// to bypass by accident.
/// </summary>
public class Batch : BaseEntity
{
    private readonly List<SensorReading> _sensorReadings = new();
    private readonly List<Alert> _alerts = new();
    private readonly List<Shipment> _shipments = new();

    // Tolerance before a breach is treated as Critical instead of just a Warning.
    private const decimal WarningToleranceC = 1.0m;

    public string BatchNumber { get; private set; } = default!;
    public string DrugName { get; private set; } = default!;
    public string ManufacturerName { get; private set; } = default!;
    public int QuantityUnits { get; private set; }
    public DateTime ManufacturedDateUtc { get; private set; }
    public DateTime ExpiryDateUtc { get; private set; }
    public TemperatureRange SafeRange { get; private set; } = default!;
    public BatchStatus Status { get; private set; } = BatchStatus.Active;

    public IReadOnlyCollection<SensorReading> SensorReadings => _sensorReadings.AsReadOnly();
    public IReadOnlyCollection<Alert> Alerts => _alerts.AsReadOnly();
    public IReadOnlyCollection<Shipment> Shipments => _shipments.AsReadOnly();

    // EF Core materializes entities through this — never call it from app code.
    private Batch() { }

    public Batch(
        string batchNumber,
        string drugName,
        string manufacturerName,
        int quantityUnits,
        DateTime manufacturedDateUtc,
        DateTime expiryDateUtc,
        TemperatureRange safeRange)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("Batch number is required.", nameof(batchNumber));
        if (string.IsNullOrWhiteSpace(drugName))
            throw new ArgumentException("Drug name is required.", nameof(drugName));
        if (quantityUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityUnits), "Quantity must be positive.");
        if (expiryDateUtc <= manufacturedDateUtc)
            throw new InvalidBatchDatesException(manufacturedDateUtc, expiryDateUtc);

        BatchNumber = batchNumber;
        DrugName = drugName;
        ManufacturerName = manufacturerName;
        QuantityUnits = quantityUnits;
        ManufacturedDateUtc = manufacturedDateUtc;
        ExpiryDateUtc = expiryDateUtc;
        SafeRange = safeRange;
        Status = BatchStatus.Active;
    }

    /// <summary>
    /// THE core rule of the whole system. Adds a reading, checks it against the safe
    /// range, and raises an Alert (quarantining the batch on a critical breach) when
    /// needed. Returns the Alert raised, or null if the reading was within range.
    /// Nothing about cold-chain safety can happen except through this method.
    /// </summary>
    public Alert? RecordReading(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.BatchId != Id)
            throw new ArgumentException("This reading belongs to a different batch.", nameof(reading));

        _sensorReadings.Add(reading);

        if (!SafeRange.IsBreach(reading.TemperatureC))
            return null;

        var overBy = SafeRange.DistanceOutsideRange(reading.TemperatureC);
        var severity = overBy <= WarningToleranceC ? AlertSeverity.Warning : AlertSeverity.Critical;

        var message = severity == AlertSeverity.Critical
            ? $"CRITICAL: Batch {BatchNumber} recorded {reading.TemperatureC}C — outside safe range [{SafeRange}]. Batch quarantined."
            : $"WARNING: Batch {BatchNumber} recorded {reading.TemperatureC}C — approaching the edge of its safe range [{SafeRange}].";

        var alert = Alert.Raise(Id, reading.Id, severity, message);
        _alerts.Add(alert);

        if (severity == AlertSeverity.Critical && Status != BatchStatus.Recalled)
            Quarantine();

        return alert;
    }

    public void Quarantine()
    {
        if (Status is BatchStatus.Delivered or BatchStatus.Recalled)
            throw new InvalidBatchStateTransitionException(BatchNumber, Status.ToString(), "be quarantined");
        Status = BatchStatus.Quarantined;
    }

    public void ClearQuarantine()
    {
        if (Status != BatchStatus.Quarantined)
            throw new InvalidBatchStateTransitionException(BatchNumber, Status.ToString(), "have its quarantine cleared");
        Status = BatchStatus.Active;
    }

    public void Recall() => Status = BatchStatus.Recalled;

    /// <summary>Creates and attaches a shipment for this batch. Blocked while quarantined/recalled.</summary>
    public Shipment Ship(string originLocation, string destinationLocation, string? courierName)
    {
        if (Status is BatchStatus.Quarantined or BatchStatus.Recalled)
            throw new InvalidBatchStateTransitionException(BatchNumber, Status.ToString(), "be shipped");

        var shipment = new Shipment(Id, originLocation, destinationLocation, courierName);
        shipment.Depart();
        _shipments.Add(shipment);
        Status = BatchStatus.InTransit;
        return shipment;
    }

    public void MarkDelivered()
    {
        if (Status != BatchStatus.InTransit)
            throw new InvalidBatchStateTransitionException(BatchNumber, Status.ToString(), "be marked as delivered");
        Status = BatchStatus.Delivered;
    }
}
