namespace MedGuard.IngestionBridge;

public class BridgeOptions
{
    public string MqttHost { get; set; } = "localhost";
    public int MqttPort { get; set; } = 11883;

    /// <summary>
    /// Members of the same share group SPLIT the messages between them (each
    /// message goes to exactly one), the MQTT equivalent of a Kafka consumer group.
    /// </summary>
    public string ShareGroup { get; set; } = "ingestion-bridge";

    /// <summary>
    /// Must be unique per running instance AND stable across restarts of that
    /// instance. The persistent MQTT session (and its unacknowledged messages)
    /// is tied to the client id, so a random id would orphan it on every restart.
    /// </summary>
    public string InstanceId { get; set; } = "1";

    public string KafkaBootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Max messages received from MQTT but not yet confirmed by Kafka. This is
    /// the backpressure valve: when Kafka is slow or down, the bridge stops
    /// taking messages and the broker holds them instead of us holding them in RAM.
    /// </summary>
    public int MaxInFlight { get; set; } = 1000;
}
