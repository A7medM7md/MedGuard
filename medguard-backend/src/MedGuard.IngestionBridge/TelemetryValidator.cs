using System.Text.Json;
using MedGuard.Contracts.Devices;

namespace MedGuard.IngestionBridge;

/// <summary>
/// The bridge only checks what it MUST check to route a message: the payload
/// is valid JSON of the right shape, and has the key we partition by. Business
/// rules (does this batch exist? is the temperature a breach?) belong to the
/// consumer that owns that data, not to the transport layer.
/// </summary>
public static class TelemetryValidator
{
    public static (DeviceTelemetry? Telemetry, string? Error) Validate(string topic, ReadOnlySpan<byte> payload)
    {
        var topicDevice = MqttTopics.DeviceCodeFrom(topic);
        if (topicDevice is null)
            return (null, $"unexpected topic '{topic}'");

        DeviceTelemetry? t;
        try
        {
            t = JsonSerializer.Deserialize<DeviceTelemetry>(payload, ContractJson.Options);
        }
        catch (JsonException ex)
        {
            return (null, $"invalid JSON: {ex.Message}");
        }

        if (t is null) return (null, "empty payload");
        if (t.MessageId == Guid.Empty) return (null, "missing messageId");
        if (t.BatchId == Guid.Empty) return (null, "missing batchId (needed as partition key)");

        // A device may only publish as itself. Without this check, a compromised
        // device could inject readings for any other device's batch.
        if (!string.Equals(t.DeviceCode, topicDevice, StringComparison.Ordinal))
            return (null, $"deviceCode '{t.DeviceCode}' does not match topic device '{topicDevice}'");

        // Physically impossible for a cold-chain sensor, so it's a sensor fault,
        // not a breach. A breach is 9 °C, not 900 °C.
        if (t.TemperatureC is < -100 or > 100)
            return (null, $"temperature {t.TemperatureC} °C out of sensor range");

        return (t, null);
    }
}
