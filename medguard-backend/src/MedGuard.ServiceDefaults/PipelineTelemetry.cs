using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Confluent.Kafka;
using MedGuard.Contracts.Tracing;

namespace MedGuard.ServiceDefaults;

/// <summary>
/// The pipeline's spans and metrics (ADR 0011). One ActivitySource and one Meter
/// shared by all services; the "service.name" resource attribute tells them apart.
///
/// Metric names are OpenTelemetry style (dots). Prometheus turns them into e.g.
/// <c>medguard_readings_processed_total</c>. Dimensions are kept to small, fixed
/// sets (outcome=...), never ids: one time series per batch id would melt Prometheus.
/// </summary>
public static class PipelineTelemetry
{
    public const string Name = "MedGuard.Pipeline";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> BridgeMessages = Meter.CreateCounter<long>(
        "medguard.bridge.messages", "{message}", "MQTT messages handled by the bridge; outcome=forwarded|dlq");

    public static readonly Counter<long> ReadingsProcessed = Meter.CreateCounter<long>(
        "medguard.readings.processed", "{reading}", "Readings finished by the worker; outcome=recorded|duplicate|dlq");

    public static readonly Histogram<double> EndToEndLatency = Meter.CreateHistogram<double>(
        "medguard.readings.e2e_latency", "ms", "Device sampled the reading -> committed to SQL");

    public static readonly Histogram<long> BatchSize = Meter.CreateHistogram<long>(
        "medguard.worker.batch_size", "{message}", "Messages per worker transaction");

    public static readonly Histogram<double> DbDuration = Meter.CreateHistogram<double>(
        "medguard.worker.db_duration", "ms", "Time in the database per worker transaction");

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>(
        "medguard.outbox.published", "{message}", "Outbox rows sent to Kafka; result=ok|failed");

    public static readonly Counter<long> NotifierAlerts = Meter.CreateCounter<long>(
        "medguard.notifier.alerts", "{alert}", "Alert events seen by the notifier; result=pushed|throttled|duplicate");

    public static readonly Counter<long> NotifierReadingPushes = Meter.CreateCounter<long>(
        "medguard.notifier.reading_pushes", "{push}", "Conflated ReadingsUpdated pushes to browsers");

    private static long _consumerLag;

    /// <summary>Updated by the consumer every report interval; exported as a gauge.</summary>
    public static void SetConsumerLag(long lag) => Interlocked.Exchange(ref _consumerLag, lag);

    static PipelineTelemetry() =>
        Meter.CreateObservableGauge("medguard.consumer.lag", () => Interlocked.Read(ref _consumerLag),
            "{message}", "Messages in assigned partitions not yet consumed by this instance");

    /// <summary>
    /// Starts a span WITHOUT leaving it as Activity.Current. Use it when the
    /// work it measures finishes later (a Kafka delivery), so the caller's code
    /// doesn't accidentally run inside it. End it with <see cref="EndWhenDone{T}"/>.
    /// </summary>
    public static Activity? StartDetached(string name, ActivityKind kind, ActivityContext parent)
    {
        var previous = Activity.Current;
        var span = Source.StartActivity(name, kind, parent);
        Activity.Current = previous;
        return span;
    }

    /// <summary>Ends <paramref name="span"/> when <paramref name="task"/> completes, marking failures.</summary>
    public static Task<T> EndWhenDone<T>(Task<T> task, Activity? span)
    {
        return span is null ? task : Await(task, span);

        static async Task<T> Await(Task<T> task, Activity span)
        {
            try { return await task; }
            catch (Exception ex) { span.SetStatus(ActivityStatusCode.Error, ex.Message); throw; }
            finally { span.Dispose(); }
        }
    }

    // --- Kafka header propagation -------------------------------------------

    /// <summary>Puts the current span's traceparent on an outgoing Kafka message.</summary>
    public static void Inject(Headers headers, Activity? activity = null)
    {
        var id = (activity ?? Activity.Current) is { IdFormat: ActivityIdFormat.W3C } a ? a.Id : null;
        if (id is not null)
            headers.Add(TraceContext.TraceParent, Encoding.ASCII.GetBytes(id));
    }

    /// <summary>The traceparent of an incoming Kafka message, or null.</summary>
    public static string? TraceParentOf(Headers? headers) =>
        headers?.TryGetLastBytes(TraceContext.TraceParent, out var bytes) == true ? Encoding.ASCII.GetString(bytes) : null;
}
