using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Repositories;

public class OrgSettingsRepository : EfRepository<OrgSettings>, IOrgSettingsRepository
{
    public OrgSettingsRepository(MedGuardDbContext context) : base(context) { }

    public async Task<OrgSettings?> GetSingletonAsync(CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(ct);
}
