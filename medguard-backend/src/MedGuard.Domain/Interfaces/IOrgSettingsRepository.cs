using MedGuard.Domain.Entities;

namespace MedGuard.Domain.Interfaces;

public interface IOrgSettingsRepository : IRepository<OrgSettings>
{
    /// <summary>The one settings row, if it's been created yet.</summary>
    Task<OrgSettings?> GetSingletonAsync(CancellationToken ct = default);
}
