using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface ISensorReadingRepository : IRepository<SensorReading>
{
    Task<IReadOnlyList<SensorReading>> GetByBatchIdAsync(Guid batchId, int take = 100, CancellationToken ct = default);
    Task<SensorReading?> GetLatestForBatchAsync(Guid batchId, CancellationToken ct = default);
}
