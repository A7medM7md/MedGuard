using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MedGuard.API.Realtime;

/// <summary>What the notifier's consume loop reports about itself (ADR 0010).</summary>
public sealed class NotifierHealth
{
    private long _lastLoopTicks, _brokerDownTicks;
    private int _assigned;

    public void LoopTick(int assignedPartitions)
    {
        Interlocked.Exchange(ref _lastLoopTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _assigned, assignedPartitions);
    }

    /// <summary>librdkafka reported "all brokers down". It never reports "back up",
    /// so this decays: healthy again once no new error has arrived for 15s.</summary>
    public void BrokerDown() => Interlocked.Exchange(ref _brokerDownTicks, DateTime.UtcNow.Ticks);

    public bool KafkaHealthy =>
        Volatile.Read(ref _assigned) > 0
        && DateTime.UtcNow - new DateTime(Interlocked.Read(ref _brokerDownTicks), DateTimeKind.Utc) > TimeSpan.FromSeconds(15);

    public DateTime? LastLoopAtUtc => Interlocked.Read(ref _lastLoopTicks) is var t and > 0 ? new DateTime(t, DateTimeKind.Utc) : null;
    public int AssignedPartitions => Volatile.Read(ref _assigned);
}

/// <summary>
/// The API stays READY when the live feed is down: every endpoint still works,
/// and the page falls back to polling. So this reports Degraded, never Unhealthy.
/// </summary>
public sealed class NotifierHealthCheck(NotifierHealth health) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object> { ["assignedPartitions"] = health.AssignedPartitions };
        if (health.LastLoopAtUtc is not { } last || DateTime.UtcNow - last > TimeSpan.FromSeconds(30))
            return Task.FromResult(HealthCheckResult.Degraded("notifier loop not running: live feed down, clients polling", data: data));
        if (!health.KafkaHealthy)
            return Task.FromResult(HealthCheckResult.Degraded("Kafka unreachable or no partitions assigned", data: data));
        return Task.FromResult(HealthCheckResult.Healthy(data: data));
    }
}
