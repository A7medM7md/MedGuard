using MedGuard.Domain.Entities;
using MedGuard.Domain.Enums;
using MedGuard.Domain.Exceptions;
using MedGuard.Domain.ValueObjects;

namespace MedGuard.UnitTests.Domain;

/// <summary>
/// Batch.RecordReading is THE core rule of the system: every path (HTTP, Kafka
/// single, Kafka batch) goes through it. Safe range here is 2–8 °C, so the
/// warning tolerance is 15% of 6 = 0.9 °C beyond either edge.
/// </summary>
public class BatchRecordReadingTests
{
    private static Batch NewBatch() => new("B-001", "Vaccine", "Maker", 100,
        DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow.AddYears(1), new TemperatureRange(2m, 8m));

    private static SensorReading Reading(Batch batch, decimal temperature) => new(batch.Id, "DEV-1", temperature);

    [Fact]
    public void Safe_reading_is_recorded_without_an_alert()
    {
        var batch = NewBatch();

        var alert = batch.RecordReading(Reading(batch, 5m));

        Assert.Null(alert);
        Assert.Single(batch.SensorReadings);
        Assert.Equal(BatchStatus.Active, batch.Status);
    }

    [Theory]
    [InlineData(8.5)]   // 0.5 above max, within the 0.9 tolerance
    [InlineData(1.2)]   // 0.8 below min
    public void Small_breach_raises_a_warning_and_keeps_the_batch_active(double temperature)
    {
        var batch = NewBatch();

        var alert = batch.RecordReading(Reading(batch, (decimal)temperature));

        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Equal(BatchStatus.Active, batch.Status);
    }

    [Fact]
    public void Critical_breach_raises_a_critical_alert_and_quarantines()
    {
        var batch = NewBatch();

        var alert = batch.RecordReading(Reading(batch, 12m));

        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Equal(BatchStatus.Quarantined, batch.Status);
        Assert.Single(batch.Alerts);
    }

    [Fact]
    public void Critical_breach_with_auto_quarantine_off_leaves_status_alone()
    {
        var batch = NewBatch();

        var alert = batch.RecordReading(Reading(batch, 12m), autoQuarantineOnBreach: false);

        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Equal(BatchStatus.Active, batch.Status);
    }

    [Fact]
    public void Critical_breach_on_an_already_quarantined_batch_is_still_recorded()
    {
        var batch = NewBatch();
        batch.RecordReading(Reading(batch, 12m));

        var second = batch.RecordReading(Reading(batch, 13m));

        Assert.NotNull(second);
        Assert.Equal(2, batch.SensorReadings.Count);
        Assert.Equal(BatchStatus.Quarantined, batch.Status);
    }

    [Fact]
    public void Critical_breach_on_a_recalled_batch_alerts_but_stays_recalled()
    {
        var batch = NewBatch();
        batch.Recall();

        var alert = batch.RecordReading(Reading(batch, 12m));

        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Equal(BatchStatus.Recalled, batch.Status);
    }

    /// <summary>
    /// Regression test for ADR 0008: the method used to add the reading and the
    /// alert BEFORE Quarantine() threw, leaving a half-applied aggregate. That
    /// broke the whole transaction when a batch of readings shared one DbContext.
    /// </summary>
    [Fact]
    public void Rejected_reading_leaves_the_aggregate_completely_untouched()
    {
        var batch = NewBatch();
        batch.Ship("Factory", "Pharmacy", null);
        batch.MarkDelivered();

        Assert.Throws<InvalidBatchStateTransitionException>(() => batch.RecordReading(Reading(batch, 12m)));

        Assert.Empty(batch.SensorReadings);
        Assert.Empty(batch.Alerts);
        Assert.Equal(BatchStatus.Delivered, batch.Status);
    }

    [Fact]
    public void Reading_for_another_batch_is_refused_without_side_effects()
    {
        var batch = NewBatch();
        var foreign = new SensorReading(Guid.NewGuid(), "DEV-1", 12m);

        Assert.Throws<ArgumentException>(() => batch.RecordReading(foreign));

        Assert.Empty(batch.SensorReadings);
        Assert.Empty(batch.Alerts);
    }
}
