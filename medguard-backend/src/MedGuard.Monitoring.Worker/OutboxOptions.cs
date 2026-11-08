namespace MedGuard.Monitoring.Worker;

public class OutboxOptions
{
    /// <summary>Off switch, e.g. to watch rows pile up in OutboxMessages while Kafka is down.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often an idle dispatcher checks for new rows. This is the
    /// outbox's added latency: an alert waits up to this long before it's published.</summary>
    public int PollIntervalMs { get; set; } = 500;

    public int BatchSize { get; set; } = 100;

    /// <summary>Kafka delivery timeout. Row locks are held while we wait, so keep it short.</summary>
    public int ProduceTimeoutMs { get; set; } = 10_000;

    /// <summary>How long published rows stay in the table before cleanup deletes them.</summary>
    public int RetentionHours { get; set; } = 24;
}
