namespace MedGuard.Contracts.Kafka;

/// <summary>Topic names. They're created by docker-compose's kafka-init job; auto-create is off.</summary>
public static class KafkaTopics
{
    /// <summary>Raw device telemetry, keyed by BatchId so one batch's readings stay in order.</summary>
    public const string SensorReadings = "sensor-readings";

    /// <summary>Messages that can never be processed (bad JSON, failed validation),
    /// parked for a human instead of blocking their partition.</summary>
    public const string SensorReadingsDlq = "sensor-readings.dlq";

    /// <summary>Alert events (<c>BatchAlertRaised</c>), published via the outbox, keyed by BatchId.</summary>
    public const string BatchAlerts = "batch-alerts";
}

/// <summary>
/// Kafka message headers. Metadata ABOUT the message travels here, not in the
/// payload, so consumers can route, dedupe or trace without parsing the body.
/// </summary>
public static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string DeviceCode = "device-code";
    public const string Source = "source";
    public const string MqttTopic = "mqtt-topic";
    public const string ReceivedAtUtc = "received-at-utc";
    public const string DlqReason = "dlq-reason";

    /// <summary>The contract's type name, e.g. <c>BatchAlertRaised</c>.</summary>
    public const string EventType = "event-type";

    /// <summary>When the event happened (the outbox row's time), not when it was published.</summary>
    public const string OccurredAtUtc = "occurred-at-utc";
}
