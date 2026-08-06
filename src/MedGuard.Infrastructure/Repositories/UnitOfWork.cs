using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;

namespace MedGuard.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly MedGuardDbContext _context;

    public UnitOfWork(
        MedGuardDbContext context,
        IBatchRepository batches,
        ISensorReadingRepository sensorReadings,
        IAlertRepository alerts,
        IShipmentRepository shipments)
    {
        _context = context;
        Batches = batches;
        SensorReadings = sensorReadings;
        Alerts = alerts;
        Shipments = shipments;
    }

    public IBatchRepository Batches { get; }
    public ISensorReadingRepository SensorReadings { get; }
    public IAlertRepository Alerts { get; }
    public IShipmentRepository Shipments { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
