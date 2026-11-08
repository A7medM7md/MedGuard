using System.Text;
using Confluent.Kafka;
using MedGuard.Contracts.Kafka;
using MedGuard.Infrastructure.Outbox;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedGuard.Monitoring.Worker;

/// <summary>
/// Publishes committed <see cref="OutboxMessage"/> rows to Kafka (ADR 0006).
///
/// Delivery is at-least-once: if we crash after Kafka acks but before the
/// "processed" UPDATE commits, the same rows are published again on restart.
/// That's why every message carries the outbox Id as its <c>message-id</c> header.
///
/// Safe to run in every worker instance: each poll locks the rows it claims with
/// UPDLOCK + READPAST, so a second dispatcher skips them instead of publishing
/// them twice or waiting on the lock.
/// </summary>
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _log;
    private readonly IProducer<string, byte[]> _producer;

    private long _published, _failed;

    public OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options,
        IOptions<MonitoringOptions> monitoring, ILogger<OutboxDispatcher> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
        _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = monitoring.Value.KafkaBootstrapServers,
            ClientId = $"monitoring-{monitoring.Value.InstanceId}-outbox",
            Acks = Acks.All,
            EnableIdempotence = true,
            // We hold row locks in SQL while waiting for these acks, so a broker
            // outage must fail fast rather than after the 5-minute default.
            MessageTimeoutMs = _options.ProduceTimeoutMs,
            SocketNagleDisable = true,
        })
        .SetErrorHandler((_, e) => _log.LogWarning("Kafka: {Reason}", e.Reason))
        .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Outbox dispatcher started: poll={Poll}ms batch={Batch}",
            _options.PollIntervalMs, _options.BatchSize);

        var nextCleanup = DateTime.UtcNow;
        var nextReport = DateTime.UtcNow.AddSeconds(10);
        var failureDelay = TimeSpan.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (claimed, failed) = await DispatchBatchAsync(ct);

                if (failed > 0)
                {
                    // Broker trouble: back off 1s, 2s, 4s ... 30s instead of hammering it.
                    failureDelay = TimeSpan.FromMilliseconds(Math.Min(
                        30_000, Math.Max(1_000, failureDelay.TotalMilliseconds * 2)));
                    await Task.Delay(failureDelay, ct);
                    continue;
                }

                failureDelay = TimeSpan.Zero;

                if (DateTime.UtcNow >= nextCleanup)
                {
                    await DeletePublishedAsync(ct);
                    nextCleanup = DateTime.UtcNow.AddMinutes(10);
                }

                if (DateTime.UtcNow >= nextReport)
                {
                    _log.LogInformation("outbox published={Published} failed={Failed}",
                        Interlocked.Read(ref _published), Interlocked.Read(ref _failed));
                    nextReport = DateTime.UtcNow.AddSeconds(10);
                }

                // A full batch means there's probably more waiting: go again right away.
                if (claimed < _options.BatchSize)
                    await Task.Delay(_options.PollIntervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database is down. Nothing is lost: the rows stay
                // pending and the next poll picks them up.
                _log.LogWarning(ex, "Outbox poll failed; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<(int Claimed, int Failed)> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();

        // The locks taken by the SELECT live until this transaction ends, so the
        // claim, the publish and the "processed" UPDATE are one critical section.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // UPDLOCK : claim the rows. A second dispatcher can't take them too.
        // READPAST: ...and it SKIPS rows someone else has claimed instead of blocking,
        //           so N dispatchers drain N disjoint sets in parallel.
        // ROWLOCK : lock rows, not pages, so the API inserting new rows isn't blocked.
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT TOP ({_options.BatchSize}) *
                FROM OutboxMessages WITH (UPDLOCK, ROWLOCK, READPAST)
                WHERE ProcessedAtUtc IS NULL
                ORDER BY OccurredAtUtc
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return (0, 0);

        // Send the whole batch, THEN wait. Awaiting each one in turn would cost a
        // full broker round trip per message. The idempotent producer keeps
        // per-partition order even with several requests in flight.
        var sends = batch.Select(m => (Message: m, Delivery: _producer.ProduceAsync(m.Topic, ToKafka(m)))).ToList();

        var failed = 0;
        foreach (var (message, delivery) in sends)
        {
            try
            {
                await delivery;
                message.ProcessedAtUtc = DateTime.UtcNow;
            }
            catch (ProduceException<string, byte[]> ex)
            {
                failed++;
                message.Attempts++;
                message.LastError = Truncate(ex.Error.Reason, 1000);
            }
        }

        // CancellationToken.None: Kafka has already acked these. Abandoning the
        // UPDATE now would only guarantee a duplicate publish on the next run.
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);

        Interlocked.Add(ref _published, batch.Count - failed);
        Interlocked.Add(ref _failed, failed);
        if (failed > 0)
            _log.LogWarning("Outbox: {Failed}/{Total} messages failed to publish: {Reason}",
                failed, batch.Count, batch.First(m => m.ProcessedAtUtc is null).LastError);

        return (batch.Count, failed);
    }

    private static Message<string, byte[]> ToKafka(OutboxMessage m) => new()
    {
        Key = m.MessageKey,
        Value = Encoding.UTF8.GetBytes(m.Payload),
        Headers = new Headers
        {
            { KafkaHeaders.MessageId, Encoding.UTF8.GetBytes(m.Id.ToString()) },
            { KafkaHeaders.EventType, Encoding.UTF8.GetBytes(m.Type) },
            { KafkaHeaders.Source, Encoding.UTF8.GetBytes("outbox") },
            { KafkaHeaders.OccurredAtUtc, Encoding.UTF8.GetBytes(m.OccurredAtUtc.ToString("O")) },
        },
    };

    /// <summary>Published rows are only kept for debugging; Kafka is the record now.</summary>
    private async Task DeletePublishedAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        var cutoff = DateTime.UtcNow.AddHours(-_options.RetentionHours);

        var deleted = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _log.LogInformation("Outbox: deleted {Count} rows published before {Cutoff:u}", deleted, cutoff);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public override void Dispose()
    {
        // Anything still queued inside the client gets a few seconds to go out.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        base.Dispose();
    }
}
