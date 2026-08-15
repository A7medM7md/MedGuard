using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces
{
    public interface IDeviceRepository : IRepository<Device>
    {
        Task<Device?> GetByDeviceCodeAsync(string deviceCode, CancellationToken ct = default);
    }
}
