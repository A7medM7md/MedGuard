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

    /// <summary>
    /// Every 5s, from INSIDE the consume loop (ADR 0010). "Connected" only proves
    /// the WebSocket is up; this proves the feed behind it is alive. If heartbeats
    /// stop, or say Kafka is down, the page falls back to polling.
    /// </summary>
    Task FeedHeartbeat(FeedStatus status);
}

public record FeedStatus(bool KafkaHealthy, int AssignedPartitions, DateTime ServerTimeUtc);

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
    int SuppressedCount,
    string? TraceId = null); // the reading's trace (ADR 0011): paste it into Seq to see the whole journey

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
