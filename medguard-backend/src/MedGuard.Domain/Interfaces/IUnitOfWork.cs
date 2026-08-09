namespace MedGuard.Domain.Interfaces;

public interface IUnitOfWork
{
    IBatchRepository Batches { get; }
    ISensorReadingRepository SensorReadings { get; }
    IAlertRepository Alerts { get; }
    IShipmentRepository Shipments { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
