using MedGuard.Application.Interfaces;
using MedGuard.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MedGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<IColdChainMonitoringService, ColdChainMonitoringService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<ISettingsService, SettingsService>();

        return services;
    }
}
