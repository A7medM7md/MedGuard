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
        services.AddDbContext<MedGuardDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("MedGuardDb")));

        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ISensorReadingRepository, SensorReadingRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
