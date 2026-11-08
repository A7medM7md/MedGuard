using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

namespace MedGuard.Monitoring.Worker;

/// <summary>What the consume loop reports about itself, read by <see cref="ConsumerLoopHealthCheck"/>.</summary>
public sealed class WorkerHealth
{
    private long _lastLoopTicks, _retryingSinceTicks;

    public DateTime? LastLoopAtUtc => Interlocked.Read(ref _lastLoopTicks) is var t and > 0 ? new DateTime(t, DateTimeKind.Utc) : null;
    public DateTime? RetryingSinceUtc => Interlocked.Read(ref _retryingSinceTicks) is var t and > 0 ? new DateTime(t, DateTimeKind.Utc) : null;

    public void LoopTick() => Interlocked.Exchange(ref _lastLoopTicks, DateTime.UtcNow.Ticks);

    /// <summary>Marks the start of a retry streak (keeps the FIRST failure time).</summary>
    public void Retrying() => Interlocked.CompareExchange(ref _retryingSinceTicks, DateTime.UtcNow.Ticks, 0);

    public void Progressing() => Interlocked.Exchange(ref _retryingSinceTicks, 0);
}

/// <summary>
/// LIVENESS: the loop calls Consume() at least every ~30s even while retrying
/// (that's how it stays in the consumer group). Silence for 60s means it's stuck,
/// e.g. blocked forever on a call, and a restart is the right fix.
/// Under readiness the same check reports "stuck retrying" (DB down) as Degraded:
/// alive, but not keeping up.
/// </summary>
public sealed class ConsumerLoopHealthCheck(WorkerHealth health) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (health.LastLoopAtUtc is not { } last)
            return Task.FromResult(HealthCheckResult.Degraded("consume loop not started yet"));

        if (now - last > TimeSpan.FromSeconds(60))
            return Task.FromResult(HealthCheckResult.Unhealthy($"consume loop silent for {(now - last).TotalSeconds:0}s"));

        if (health.RetryingSinceUtc is { } since && now - since > TimeSpan.FromSeconds(30))
            return Task.FromResult(HealthCheckResult.Degraded($"retrying the same messages for {(now - since).TotalSeconds:0}s"));

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}

/// <summary>
/// READINESS for the alert path: alerts are committed but not reaching Kafka
/// when the circuit is open, or when rows have been pending for over a minute.
/// Degraded, not Unhealthy: readings are still being recorded.
/// </summary>
public sealed class OutboxHealthCheck(OutboxDispatcher dispatcher, IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var oldest = await scope.ServiceProvider.GetRequiredService<MedGuardDbContext>().OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .MinAsync(m => (DateTime?)m.OccurredAtUtc, ct);
        var age = oldest is { } o ? DateTime.UtcNow - o : TimeSpan.Zero;
        var data = new Dictionary<string, object>
        {
            ["circuit"] = dispatcher.Circuit.CircuitState.ToString(),
            ["oldestPendingSeconds"] = (int)age.TotalSeconds,
        };

        if (dispatcher.Circuit.CircuitState is CircuitState.Open or CircuitState.Isolated)
            return HealthCheckResult.Degraded("outbox circuit open: Kafka unreachable, alerts queued in SQL", data: data);
        if (age > TimeSpan.FromSeconds(60))
            return HealthCheckResult.Degraded($"oldest unpublished alert is {age.TotalSeconds:0}s old", data: data);
        return HealthCheckResult.Healthy(data: data);
    }
}
