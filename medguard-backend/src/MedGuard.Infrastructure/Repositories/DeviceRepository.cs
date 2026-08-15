using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories;

public class DeviceRepository : EfRepository<Device>, IDeviceRepository
{
    public DeviceRepository(MedGuardDbContext context) : base(context) { }

    public async Task<Device?> GetByDeviceCodeAsync(string deviceCode, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, ct);
}