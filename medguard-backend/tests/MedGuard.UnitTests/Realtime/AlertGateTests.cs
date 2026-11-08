using MedGuard.API.Realtime;
using MedGuard.Contracts.Events;

namespace MedGuard.UnitTests.Realtime;

/// <summary>Dedupe + throttle rules from ADR 0007, driven by a fake clock.</summary>
public class AlertGateTests
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Guid _batch = Guid.NewGuid();
    private readonly AlertGate _gate;

    public AlertGateTests() =>
        _gate = new AlertGate(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), () => _now);

    private BatchAlertRaised Event(string severity = "Warning", string status = "Active") =>
        new(Guid.NewGuid(), _batch, "B-1", "Drug", severity, "msg", 9.5m, status, "DEV-1", _now);

    [Fact]
    public void Same_message_id_twice_is_a_duplicate()
    {
        var id = Guid.NewGuid();

        Assert.NotNull(_gate.Admit(id, Event()));
        Assert.Null(_gate.Admit(id, Event()));
        Assert.Equal(1, _gate.Duplicates);
    }

    [Fact]
    public void Repeats_inside_the_window_are_counted_then_reported_on_the_next_push()
    {
        _gate.Admit(Guid.NewGuid(), Event());
        _now = _now.AddSeconds(1);
        Assert.Null(_gate.Admit(Guid.NewGuid(), Event()));
        Assert.Null(_gate.Admit(Guid.NewGuid(), Event()));

        _now = _now.AddSeconds(31);
        var next = _gate.Admit(Guid.NewGuid(), Event());

        Assert.Equal(2, next!.SuppressedCount);
        Assert.Equal(2, _gate.Throttled);
    }

    [Fact]
    public void Escalation_is_never_throttled()
    {
        _gate.Admit(Guid.NewGuid(), Event("Warning", "Active"));
        _now = _now.AddSeconds(1);

        Assert.NotNull(_gate.Admit(Guid.NewGuid(), Event("Critical", "Quarantined")));
    }

    [Fact]
    public void Message_ids_are_forgotten_after_the_dedupe_window()
    {
        var id = Guid.NewGuid();
        _gate.Admit(id, Event());

        _now = _now.AddMinutes(11);

        Assert.NotNull(_gate.Admit(id, Event()));
    }
}
