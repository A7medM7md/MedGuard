using MedGuard.Contracts.Devices;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MedGuard.DeviceSimulator;

/// <summary>
/// The other side of pub/sub: subscribes with a wildcard and prints everything.
/// The publishers don't know this exists. That's the whole point of a broker.
/// </summary>
public static class Watcher
{
    public static async Task RunAsync(MqttOptions mqtt, string topicFilter, CancellationToken ct)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        client.ApplicationMessageReceivedAsync += e =>
        {
            var msg = e.ApplicationMessage;
            // Retained messages are delivered immediately on subscribe. This is how
            // a fresh dashboard learns every device's last status without waiting.
            var retained = msg.Retain ? " [retained]" : "";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {msg.Topic} (QoS {(int)msg.QualityOfServiceLevel}){retained}");
            Console.WriteLine($"         {msg.ConvertPayloadToString()}");
            return Task.CompletedTask;
        };

        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId($"watcher-{Guid.NewGuid():N}")
            .WithCleanStart()
            .Build(), ct);

        await client.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build(), ct);

        Console.WriteLine($"Watching '{topicFilter}' on {mqtt.Host}:{mqtt.Port}. Ctrl+C to stop.\n");
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        await client.DisconnectAsync();
    }

    public static string DefaultFilter => MqttTopics.All;
}
