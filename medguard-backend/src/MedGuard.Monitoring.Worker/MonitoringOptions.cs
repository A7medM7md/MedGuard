namespace MedGuard.Monitoring.Worker;

public class MonitoringOptions
{
    public string KafkaBootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Every instance uses the same group, so they SHARE the partitions.
    /// A different service (say, analytics) would use its own group and get every reading too.</summary>
    public string GroupId { get; set; } = "monitoring";

    public string InstanceId { get; set; } = "1";

    /// <summary>Attempts for an error we can't classify before giving up and
    /// dead-lettering. Transient errors (database down) retry forever instead.</summary>
    public int MaxAttemptsForUnknownErrors { get; set; } = 3;

    /// <summary>
    /// How long the group waits to hear from a member before declaring it dead and
    /// moving its partitions. Lower = faster failover after a crash, but a long GC
    /// pause or a slow batch can get a HEALTHY consumer kicked out. Kafka default: 45s.
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 45_000;

    /// <summary>
    /// Up to this many messages are processed in one transaction (ADR 0008).
    /// 1 = the original one-message-at-a-time path. Under a backlog, batches fill
    /// instantly from librdkafka's prefetch buffer; at low traffic they're small,
    /// so batching costs no latency when there's nothing to batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 200;

    /// <summary>How long to wait for more messages after the first one before
    /// processing a partial batch. This is the most latency batching can add.</summary>
    public int MaxBatchWaitMs { get; set; } = 20;
}
