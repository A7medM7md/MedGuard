using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Confluent.Kafka;
using MedGuard.Contracts.Devices;
using MedGuard.Contracts.Kafka;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MedGuard.IngestionBridge;

/// <summary>
/// Moves telemetry from MQTT (the device edge) into Kafka (the backend log)
/// with an at-least-once guarantee end to end:
///
///   MQTT PUBLISH --> produce to Kafka --> Kafka acks --> THEN we PUBACK to MQTT
///
/// If we crash anywhere before the PUBACK, the broker still holds the message
/// in our persistent session and redelivers it when we reconnect. The worst case
/// is a duplicate in Kafka (deduplicated downstream by MessageId), never a loss.
///
/// Throughput comes from pipelining. We don't wait for Kafka before taking the
/// next MQTT message. Each one is produced immediately and queued with its pending
/// delivery Task, and a single ack loop walks that queue IN ARRIVAL ORDER,
/// awaiting each delivery and then acknowledging. MQTT expects PUBACKs in the order
/// the PUBLISHes arrived; Kafka delivery reports across partitions can complete
/// out of order, and the queue puts them back in order.
/// </summary>
public sealed class MqttToKafkaBridge : BackgroundService
{
    private readonly BridgeOptions _options;
    private readonly ILogger<MqttToKafkaBridge> _log;
    private readonly IProducer<string, byte[]> _producer;
    private readonly BridgeStats _stats = new();
    private ChannelReader<PendingAck>? _currentQueue; // for stats only

    private string SubscriptionFilter => $"$share/{_options.ShareGroup}/{MqttTopics.AllTelemetry}";

    public MqttToKafkaBridge(IOptions<BridgeOptions> options, ILogger<MqttToKafkaBridge> log)
    {
        _options = options.Value;
        _log = log;

        // ADR 0003 conventions.
        _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = _options.KafkaBootstrapServers,
            ClientId = $"ingestion-bridge-{_options.InstanceId}",
            Acks = Acks.All,
            EnableIdempotence = true,
            Partitioner = Partitioner.Murmur2Random,
            LingerMs = 5,
            // How long librdkafka keeps retrying one message before giving up
            // with a delivery error. The default is 5 minutes; 60s surfaces a dead
            // Kafka sooner, and we recover by letting MQTT redeliver.
            MessageTimeoutMs = 60_000,
        })
        .SetErrorHandler((_, e) => _log.LogWarning("Kafka: {Reason} (fatal={Fatal})", e.Reason, e.IsFatal))
        .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = ReportStatsAsync(stoppingToken);

        // Supervisor: one iteration = one MQTT connection's lifetime. Any failure
        // (broker gone, Kafka delivery failed) ends the session; we back off and
        // start a fresh one, and the broker redelivers everything we never acked.
        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError("Session ended: {Error}. Reconnecting in {Delay}s", ex.Message, backoff.TotalSeconds);
            }

            if (stoppingToken.IsCancellationRequested) break;
            try { await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(500)), stoppingToken); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
        }
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        using var connectionLost = new CancellationTokenSource();

        // A fresh queue per connection: acks for messages from a DEAD connection
        // are meaningless (the broker will redeliver them anyway), so they're dropped.
        var pending = Channel.CreateBounded<PendingAck>(new BoundedChannelOptions(_options.MaxInFlight)
        {
            SingleReader = true,
            SingleWriter = true,
        });
        _currentQueue = pending.Reader;

        client.DisconnectedAsync += e =>
        {
            if (!stoppingToken.IsCancellationRequested)
                _log.LogWarning("MQTT disconnected: {Reason}", e.Reason);
            connectionLost.Cancel();
            return Task.CompletedTask;
        };

        client.ApplicationMessageReceivedAsync += async e =>
        {
            // We decide when the broker may forget this message, not the library.
            e.AutoAcknowledge = false;
            Interlocked.Increment(ref _stats.Received);
            // DUP = the broker already sent this to someone and never got a PUBACK
            // (e.g. our previous session died mid-flight). Counted separately so
            // received - redelivered = unique messages.
            if (e.ApplicationMessage.Dup)
                Interlocked.Increment(ref _stats.Redelivered);

            var delivery = ForwardToKafka(e.ApplicationMessage);
            try
            {
                // Blocks when MaxInFlight messages are waiting on Kafka. That's
                // backpressure: we stop reading MQTT until Kafka catches up.
                await pending.Writer.WriteAsync(new PendingAck(e, delivery), connectionLost.Token);
            }
            catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
            {
                // Shutting down or disconnected: leave it unacked, so it's redelivered.
            }
        };

        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(_options.MqttHost, _options.MqttPort)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId($"ingestion-bridge-{_options.InstanceId}")
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
            // Persistent session: on reconnect, the broker resends every QoS 1
            // message we received but never PUBACKed.
            .WithCleanStart(false)
            .WithSessionExpiryInterval(3600)
            // Tells the broker never to have more than this many unacked
            // messages in flight to us (MQTT v5 flow control).
            .WithReceiveMaximum((ushort)Math.Min(_options.MaxInFlight, ushort.MaxValue))
            .Build(), stoppingToken);

        await client.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(SubscriptionFilter).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build(), stoppingToken);

        _log.LogInformation("Bridging MQTT '{Filter}' -> Kafka '{Topic}' as instance {Instance}",
            SubscriptionFilter, KafkaTopics.SensorReadings, _options.InstanceId);

        var ackLoop = AckInOrderAsync(pending.Reader, connectionLost.Token);
        var stopRequested = Task.Delay(Timeout.Infinite, stoppingToken);

        if (await Task.WhenAny(ackLoop, stopRequested) == ackLoop)
        {
            await ackLoop; // rethrows the reason the session died
            return;
        }

        // Graceful shutdown: stop new deliveries, let in-flight ones finish and get
        // acked, THEN disconnect. Anything still unacked after 10s is redelivered
        // to the next instance, so it's slower but nothing is lost.
        _log.LogInformation("Stopping: draining {Count} in-flight message(s)", pending.Reader.Count);
        try
        {
            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.UnsubscribeAsync(SubscriptionFilter, drainTimeout.Token);
            pending.Writer.TryComplete();
            await ackLoop.WaitAsync(drainTimeout.Token);
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(), drainTimeout.Token);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Drain incomplete ({Error}); unacked messages will be redelivered", ex.Message);
        }
    }

    private async Task AckInOrderAsync(ChannelReader<PendingAck> reader, CancellationToken connectionLost)
    {
        await foreach (var item in reader.ReadAllAsync(connectionLost))
        {
            try
            {
                await item.KafkaDelivery;
            }
            catch (ProduceException<string, byte[]> ex)
            {
                // Kafka refused or timed out. Do NOT ack; end the session instead.
                // On reconnect, the broker redelivers this message and every one
                // after it (some may already be in Kafka; that's the duplicate
                // at-least-once allows).
                throw new InvalidOperationException($"Kafka delivery failed: {ex.Error.Reason}", ex);
            }

            await item.Mqtt.AcknowledgeAsync(connectionLost);
            Interlocked.Increment(ref _stats.Acked);
        }
    }

    /// <summary>Starts the Kafka produce and returns its delivery Task without awaiting it.</summary>
    private Task ForwardToKafka(MqttApplicationMessage message)
    {
        var payload = message.Payload.ToArray();
        var (telemetry, error) = TelemetryValidator.Validate(message.Topic, payload);

        var headers = new Headers
        {
            { KafkaHeaders.Source, "mqtt"u8.ToArray() },
            { KafkaHeaders.MqttTopic, Encoding.UTF8.GetBytes(message.Topic) },
            { KafkaHeaders.ReceivedAtUtc, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")) },
        };

        if (telemetry is null)
        {
            // Poison message: park it in the DLQ and ack it. Leaving it unacked
            // would mean the broker redelivers it forever and it never succeeds.
            Interlocked.Increment(ref _stats.DeadLettered);
            _log.LogWarning("DLQ: {Topic}: {Error}", message.Topic, error);
            headers.Add(KafkaHeaders.DlqReason, Encoding.UTF8.GetBytes(error!));
            return _producer.ProduceAsync(KafkaTopics.SensorReadingsDlq, new Message<string, byte[]>
            {
                Key = MqttTopics.DeviceCodeFrom(message.Topic) ?? "unknown",
                Value = payload,
                Headers = headers,
            });
        }

        headers.Add(KafkaHeaders.MessageId, Encoding.UTF8.GetBytes(telemetry.MessageId.ToString()));
        headers.Add(KafkaHeaders.DeviceCode, Encoding.UTF8.GetBytes(telemetry.DeviceCode));

        // Forward the device's original bytes, not a re-serialized copy. A newer
        // firmware may send fields this bridge doesn't know yet, and consumers
        // that do know them must still receive them.
        return _producer.ProduceAsync(KafkaTopics.SensorReadings, new Message<string, byte[]>
        {
            Key = telemetry.BatchId.ToString(),
            Value = payload,
            Headers = headers,
        });
    }

    private async Task ReportStatsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        long lastAcked = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var acked = Interlocked.Read(ref _stats.Acked);
                _log.LogInformation(
                    "received={Received} redelivered={Redelivered} acked={Acked} dlq={Dlq} in-flight={InFlight} rate={Rate:0.0}/s",
                    Interlocked.Read(ref _stats.Received), Interlocked.Read(ref _stats.Redelivered), acked,
                    Interlocked.Read(ref _stats.DeadLettered), _currentQueue?.Count ?? 0, (acked - lastAcked) / 10.0);
                lastAcked = acked;
            }
        }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // Flush anything librdkafka still has buffered (linger.ms batches).
        _producer.Flush(TimeSpan.FromSeconds(5));
    }

    public override void Dispose()
    {
        _producer.Dispose();
        base.Dispose();
    }

    private sealed record PendingAck(MqttApplicationMessageReceivedEventArgs Mqtt, Task KafkaDelivery);

    private sealed class BridgeStats
    {
        public long Received;
        public long Acked;
        public long Redelivered;
        public long DeadLettered;
    }
}
