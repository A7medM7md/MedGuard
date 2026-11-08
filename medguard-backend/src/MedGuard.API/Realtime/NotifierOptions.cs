namespace MedGuard.API.Realtime;

public class NotifierOptions
{
    /// <summary>Off by default: production has no Kafka (ADR 0001). The dev launch
    /// profile turns it on. When it's off, the hub isn't mapped, and the page polls.</summary>
    public bool Enabled { get; set; }

    public string KafkaBootstrapServers { get; set; } = "localhost:9092";

    /// <summary>How often the newest reading per batch is pushed. That's also the live temperature's max staleness.</summary>
    public int ReadingFlushIntervalMs { get; set; } = 1_000;

    /// <summary>Identical alerts (batch, severity, status) inside this window are counted, not pushed.</summary>
    public int AlertThrottleSeconds { get; set; } = 30;

    public int DedupeWindowMinutes { get; set; } = 10;
}
