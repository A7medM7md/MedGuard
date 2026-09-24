using MedGuard.Domain.Interfaces;
using MedGuard.Infrastructure.Persistence;
using MedGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Never committed to appsettings.json — production supplies it via the
        // ConnectionStrings__MedGuardDb environment variable (Development uses
        // appsettings.Development.json). Fail at startup, not on the first query.
        var connectionString = configuration.GetConnectionString("MedGuardDb")
            ?? throw new InvalidOperationException(
                "Connection string 'MedGuardDb' is missing. Set the ConnectionStrings__MedGuardDb environment variable.");

        services.AddDbContext<MedGuardDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ISensorReadingRepository, SensorReadingRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IOrgSettingsRepository, OrgSettingsRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
