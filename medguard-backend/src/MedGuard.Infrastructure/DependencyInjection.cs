using MedGuard.Application.Interfaces;
using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Outbox;
using MedGuard.Infrastructure.Persistence;
using MedGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedGuard.Infrastructure;

public static class DependencyInjection
{
    /// <param name="enableRetryOnFailure">
    /// EF's built-in retry for transient SQL errors (ADR 0010). ON for the API: a
    /// request that hits a 1-second failover gets retried instead of returning 500.
    /// OFF for the worker, deliberately: it already classifies transient errors
    /// and retries by re-reading from Kafka (ADR 0005), and its outbox dispatcher
    /// opens an explicit transaction, which EF's retrying strategy refuses
    /// ("does not support user-initiated transactions").
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        bool enableRetryOnFailure = false)
    {
        // Never committed to any appsettings file. Production supplies it via the
        // ConnectionStrings__MedGuardDb environment variable; Development uses
        // user-secrets (dotnet user-secrets set ConnectionStrings:MedGuardDb "..."
        // --project src/MedGuard.API, likewise for the worker). Fail at startup, not on the first query.
        var connectionString = configuration.GetConnectionString("MedGuardDb")
            ?? throw new InvalidOperationException(
                "Connection string 'MedGuardDb' is missing. Set the ConnectionStrings__MedGuardDb environment variable, or in Development: dotnet user-secrets set ConnectionStrings:MedGuardDb \"...\".");

        services.AddDbContext<MedGuardDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                if (enableRetryOnFailure)
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            }));

        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ISensorReadingRepository, SensorReadingRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IOrgSettingsRepository, OrgSettingsRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        // Scoped like the DbContext it writes into: same scope = same transaction.
        services.AddScoped<IOutbox, EfOutbox>();

        return services;
    }
}
