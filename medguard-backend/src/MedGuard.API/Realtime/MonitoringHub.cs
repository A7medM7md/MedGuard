using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MedGuard.API.Realtime;

/// <summary>
/// Server → browser push channel for the Monitoring page (ADR 0007).
///
/// Clients never call methods on it: they connect and listen. Everything sent
/// through it comes from <see cref="KafkaNotifier"/>.
/// It's mapped only when the notifier is enabled, so a client that fails to
/// connect knows there's no live feed and falls back to polling.
/// </summary>
[Authorize(Policy = "ApiScope")]
public sealed class MonitoringHub : Hub<IMonitoringClient>
{
    public const string Path = "/hubs/monitoring";
}

/// <summary>
/// Strongly typed client contract. <c>Clients.All.AlertRaised(x)</c> is checked by
/// the compiler; the untyped alternative, <c>SendAsync("AlertRaised", x)</c>,
/// breaks silently on a typo. The method names are what the browser subscribes
/// to (<c>connection.on('AlertRaised', ...)</c>).
/// </summary>
public interface IMonitoringClient
{
    Task AlertRaised(AlertNotification alert);
    Task ReadingsUpdated(IReadOnlyList<LiveReading> readings);
}

/// <param name="SuppressedCount">How many identical alerts (same batch, severity
/// and status) were held back by the throttle since the last one sent.</param>
public record AlertNotification(
    Guid AlertId,
    Guid BatchId,
    string BatchNumber,
    string DrugName,
    string Severity,
    string Message,
    decimal TemperatureC,
    string BatchStatus,
    string DeviceCode,
    DateTime TriggeredAtUtc,
    int SuppressedCount);

/// <summary>A reading as RECEIVED from the device, before the domain has evaluated it (see ADR 0007).</summary>
public record LiveReading(
    Guid MessageId,
    Guid BatchId,
    string DeviceCode,
    decimal TemperatureC,
    decimal? HumidityPercent,
    double? Latitude,
    double? Longitude,
    DateTime RecordedAtUtc);
