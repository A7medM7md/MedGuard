using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Enums;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedGuard.IntegrationTests;

/// <summary>
/// IColdChainMonitoringService.IngestBatchAsync against a real SQL Server
/// (ADR 0008): one transaction for many readings, with per-reading outcomes.
/// </summary>
[Collection(PipelineCollection.Name)]
public class IngestBatchTests(PipelineFixture fx)
{
    private static IngestReadingCommand Reading(Guid batchId, decimal temp, Guid? messageId = null) =>
        new(messageId ?? Guid.NewGuid(), batchId, "DEV-IT", temp, 50m, null, null, DateTime.UtcNow);

    private async Task<IReadOnlyList<IngestResult>> IngestAsync(params IngestReadingCommand[] commands)
    {
        await using var scope = fx.Scope();
        return await scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>().IngestBatchAsync(commands);
    }

    [Fact]
    public async Task Duplicates_inside_the_batch_and_across_batches_are_recorded_once()
    {
        var batch = await fx.SeedBatchAsync();
        var a = Reading(batch.Id, 5m);
        var b = Reading(batch.Id, 5.1m);

        var first = await IngestAsync(a, b, a);          // copy inside the same batch
        var second = await IngestAsync(b, Reading(batch.Id, 5.2m)); // copy in a later batch

        Assert.Equal([IngestOutcome.Recorded, IngestOutcome.Recorded, IngestOutcome.Duplicate], first.Select(r => r.Outcome));
        Assert.Equal([IngestOutcome.Duplicate, IngestOutcome.Recorded], second.Select(r => r.Outcome));

        await using var scope = fx.Scope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        Assert.Equal(3, await db.SensorReadings.CountAsync(r => r.BatchId == batch.Id));
    }

    [Fact]
    public async Task A_domain_rejection_affects_only_its_own_reading()
    {
        var healthy = await fx.SeedBatchAsync();
        var delivered = await fx.SeedBatchAsync(delivered: true);

        var results = await IngestAsync(
            Reading(healthy.Id, 5m),
            Reading(delivered.Id, 12m),   // critical on Delivered: can't quarantine -> Rejected
            Reading(healthy.Id, 5.5m));

        Assert.Equal([IngestOutcome.Recorded, IngestOutcome.Rejected, IngestOutcome.Recorded], results.Select(r => r.Outcome));
        Assert.Contains("quarantined", results[1].Error);

        await using var scope = fx.Scope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        Assert.Equal(2, await db.SensorReadings.CountAsync(r => r.BatchId == healthy.Id));
        // All-or-nothing aggregate: nothing half-applied leaked into the transaction.
        Assert.Equal(0, await db.SensorReadings.CountAsync(r => r.BatchId == delivered.Id));
        Assert.Equal(0, await db.Alerts.CountAsync(a => a.BatchId == delivered.Id));
        Assert.Equal(BatchStatus.Delivered, (await db.Batches.SingleAsync(x => x.Id == delivered.Id)).Status);
    }

    [Fact]
    public async Task Unknown_batch_is_reported_and_the_rest_still_commits()
    {
        var batch = await fx.SeedBatchAsync();

        var results = await IngestAsync(Reading(Guid.NewGuid(), 5m), Reading(batch.Id, 5m));

        Assert.Equal([IngestOutcome.BatchNotFound, IngestOutcome.Recorded], results.Select(r => r.Outcome));
    }

    [Fact]
    public async Task Readings_apply_in_order_and_every_alert_gets_an_outbox_row_in_the_same_transaction()
    {
        var batch = await fx.SeedBatchAsync();

        var results = await IngestAsync(
            Reading(batch.Id, 5m),
            Reading(batch.Id, 12m),   // critical -> quarantines
            Reading(batch.Id, 13m));  // sees the quarantine reading 2 caused; still recorded

        Assert.All(results, r => Assert.Equal(IngestOutcome.Recorded, r.Outcome));
        Assert.Equal([null, AlertSeverity.Critical, AlertSeverity.Critical], results.Select(r => r.RaisedAlert));

        await using var scope = fx.Scope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        Assert.Equal(BatchStatus.Quarantined, (await db.Batches.SingleAsync(x => x.Id == batch.Id)).Status);
        Assert.Equal(2, await db.Alerts.CountAsync(a => a.BatchId == batch.Id));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(m => m.MessageKey == batch.Id.ToString()));
    }

    [Fact]
    public async Task The_unique_index_catches_a_duplicate_the_existence_check_missed()
    {
        // Two deliveries of one message racing: both pass "exists?" and only the
        // filtered unique index on MessageId can stop the second (ADR 0005).
        var batch = await fx.SeedBatchAsync();
        var command = Reading(batch.Id, 5m);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await using var scope = fx.Scope();
                return (await scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>().IngestAsync(command)).Outcome.ToString();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                return "UniqueViolation";
            }
        }));

        Assert.Single(results, r => r == nameof(IngestOutcome.Recorded));
        Assert.All(results.Where(r => r != nameof(IngestOutcome.Recorded)),
            r => Assert.Contains(r, new[] { nameof(IngestOutcome.Duplicate), "UniqueViolation" }));

        await using var check = fx.Scope();
        var db = check.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        Assert.Equal(1, await db.SensorReadings.CountAsync(r => r.MessageId == command.MessageId));
    }
}
