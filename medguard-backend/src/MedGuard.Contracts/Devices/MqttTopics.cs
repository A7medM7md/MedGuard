namespace MedGuard.Contracts.Devices;

/// <summary>
/// The MQTT topic tree devices publish into. One subtree per device:
/// <code>
/// medguard/devices/{deviceCode}/telemetry   readings (QoS 1)
/// medguard/devices/{deviceCode}/heartbeat   battery / signal (QoS 0)
/// medguard/devices/{deviceCode}/status      "online" / "offline" (retained, set by LWT)
/// </code>
/// The device code lives in the topic, not just the payload, so subscribers can
/// filter with wildcards (<c>+</c> = exactly one level, <c>#</c> = everything below)
/// and the broker can authorize per device later (device X may only publish under X).
/// </summary>
public static class MqttTopics
{
    public const string Root = "medguard/devices";

    public static string Telemetry(string deviceCode) => $"{Root}/{deviceCode}/telemetry";
    public static string Heartbeat(string deviceCode) => $"{Root}/{deviceCode}/heartbeat";
    public static string Status(string deviceCode) => $"{Root}/{deviceCode}/status";

    /// <summary>Every device's readings — what the Kafka bridge subscribes to.</summary>
    public const string AllTelemetry = Root + "/+/telemetry";

    /// <summary>Everything under the root, all message types.</summary>
    public const string All = Root + "/#";

    /// <summary>Extracts the device code from <c>medguard/devices/{deviceCode}/...</c>.</summary>
    public static string? DeviceCodeFrom(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length >= 4 && $"{parts[0]}/{parts[1]}" == Root ? parts[2] : null;
    }
}
