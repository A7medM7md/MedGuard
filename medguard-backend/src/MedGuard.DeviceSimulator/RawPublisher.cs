using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MedGuard.DeviceSimulator;

/// <summary>
/// Publishes one hand-written message. For poking the pipeline with things a
/// well-behaved device never sends: broken JSON, spoofed device codes, etc.
/// </summary>
public static class RawPublisher
{
    public static async Task RunAsync(MqttOptions mqtt, string topic, string payload, CancellationToken ct)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId($"raw-{Guid.NewGuid():N}")
            .Build(), ct);

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        Console.WriteLine($"published to {topic}: {payload}");
        await client.DisconnectAsync();
    }
}
