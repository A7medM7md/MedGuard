using System.Diagnostics;

namespace MedGuard.Contracts.Tracing;

/// <summary>
/// W3C trace context across every hop (ADR 0011): MQTT v5 user property →
/// Kafka header → outbox row → Kafka header → SignalR payload. It's the same
/// "traceparent" key everywhere, so one trace id follows a reading from the
/// sensor to the browser.
///
/// BCL only (System.Diagnostics), no OpenTelemetry dependency: any layer,
/// including Application, can carry a trace without knowing who exports it.
/// When nothing listens, StartActivity returns null and all of this is a no-op.
/// </summary>
public static class TraceContext
{
    /// <summary>"00-{trace-id}-{span-id}-{flags}" per the W3C Trace Context spec.</summary>
    public const string TraceParent = "traceparent";

    /// <summary>The current span as a traceparent string, or null when not tracing.</summary>
    public static string? Current() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } a ? a.Id : null;

    public static ActivityContext Parse(string? traceParent) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, null, isRemote: true, out var context)
            ? context
            : default;
}
