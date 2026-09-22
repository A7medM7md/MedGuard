namespace MedGuard.Domain.Interfaces;

public interface IUnitOfWork
{
    IBatchRepository Batches { get; }
    ISensorReadingRepository SensorReadings { get; }
    IAlertRepository Alerts { get; }
    IShipmentRepository Shipments { get; }
    IDeviceRepository Devices { get; }
    IOrgSettingsRepository OrgSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
