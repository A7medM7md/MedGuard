using MedGuard.Contracts.Events;

namespace MedGuard.API.Realtime;

/// <summary>
/// Decides which alert events reach the browser. Two jobs:
///
/// 1. <b>Dedupe.</b> The outbox is at-least-once (ADR 0006), so the same event can
///    arrive twice. Its <c>message-id</c> is remembered for <c>dedupeWindow</c>.
///    Duplicates come from a dispatcher retry seconds apart, so a bounded time
///    window is enough; we never need every id ever seen.
///
/// 2. <b>Throttle.</b> Every breaching reading raises an alert (ADR 0005, open
///    issue), so one warm batch produces ~2 criticals per second. The first alert
///    for a (batch, severity, batch status) goes out immediately; repeats within
///    <c>throttleWindow</c> are only counted, and that count rides along on the
///    next one sent. A CHANGE (Warning → Critical, Active → Quarantined) is a new
///    key, so it's never held back. That's the alert an operator must see.
///
/// Not thread-safe: it's only called from the notifier's single consume loop.
/// </summary>
public sealed class AlertGate
{
    private readonly TimeSpan _dedupeWindow;
    private readonly TimeSpan _throttleWindow;
    private readonly Func<DateTime> _clock;

    private readonly Dictionary<Guid, DateTime> _seen = new();
    private readonly Queue<(Guid Id, DateTime At)> _seenOrder = new();
    private readonly Dictionary<(Guid BatchId, string Severity, string Status), (DateTime SentAt, int Suppressed)> _lastSent = new();

    public AlertGate(TimeSpan dedupeWindow, TimeSpan throttleWindow, Func<DateTime>? clock = null)
    {
        _dedupeWindow = dedupeWindow;
        _throttleWindow = throttleWindow;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public long Duplicates { get; private set; }
    public long Throttled { get; private set; }

    /// <returns>The notification to push, or null if this event must not be pushed.</returns>
    public AlertNotification? Admit(Guid messageId, BatchAlertRaised e)
    {
        var now = _clock();
        Evict(now);

        if (!_seen.TryAdd(messageId, now))
        {
            Duplicates++;
            return null;
        }
        _seenOrder.Enqueue((messageId, now));

        var key = (e.BatchId, e.Severity, e.BatchStatus);
        if (_lastSent.TryGetValue(key, out var last) && now - last.SentAt < _throttleWindow)
        {
            _lastSent[key] = last with { Suppressed = last.Suppressed + 1 };
            Throttled++;
            return null;
        }

        _lastSent[key] = (now, 0);
        return new AlertNotification(e.AlertId, e.BatchId, e.BatchNumber, e.DrugName, e.Severity, e.Message,
            e.TemperatureC, e.BatchStatus, e.DeviceCode, e.TriggeredAtUtc, SuppressedCount: last.Suppressed);
    }

    private void Evict(DateTime now)
    {
        while (_seenOrder.TryPeek(out var oldest) && now - oldest.At > _dedupeWindow)
        {
            _seenOrder.Dequeue();
            _seen.Remove(oldest.Id);
        }

        // Keys idle for a long time can't throttle anything anymore; drop them so
        // the dictionary doesn't grow with every batch ever alerted on.
        if (_lastSent.Count > 1_000)
            foreach (var stale in _lastSent.Where(kv => now - kv.Value.SentAt > _throttleWindow).Select(kv => kv.Key).ToList())
                _lastSent.Remove(stale);
    }
}
