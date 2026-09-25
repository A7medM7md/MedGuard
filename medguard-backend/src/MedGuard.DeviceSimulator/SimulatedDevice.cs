using System.Text.Json;
using MedGuard.Contracts.Devices;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MedGuard.DeviceSimulator;

/// <summary>
/// One fake IoT sensor: its own MQTT connection, its own temperature model.
/// A real device would be firmware on an ESP32 or similar, but the MQTT
/// conversation with the broker is exactly the same.
/// </summary>
public sealed class SimulatedDevice
{
    private const double SafeCenterC = 5.0; // typical vaccine range is 2-8 °C

    private readonly DeviceBinding _binding;
    private readonly MqttOptions _mqtt;
    private readonly SimulationOptions _sim;
    private readonly IMqttClient _client;
    private readonly Random _random = new();

    private double _temperature = SafeCenterC;
    private int _excursionTicksLeft;
    private int _battery = 100;
    private double _latitude = 30.0444 + Random.Shared.NextDouble() * 0.1; // around Cairo
    private double _longitude = 31.2357 + Random.Shared.NextDouble() * 0.1;
    private long _tick;

    public SimulatedDevice(DeviceBinding binding, MqttOptions mqtt, SimulationOptions sim)
    {
        _binding = binding;
        _mqtt = mqtt;
        _sim = sim;
        _client = new MqttClientFactory().CreateMqttClient();
    }

    public string Code => _binding.DeviceCode;

    public async Task RunAsync(CancellationToken ct)
    {
        // Stagger startup. 1000 devices connecting in the same millisecond is a
        // "thundering herd"; real fleets see this after a broker restart.
        await Task.Delay(_random.Next(0, 1000), ct);

        var interval = TimeSpan.FromSeconds(_sim.IntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(ct);
                await PublishReadingAsync(ct);
                if (++_tick % _sim.HeartbeatEvery == 0)
                    await PublishHeartbeatAsync(ct);

                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A real device would buffer readings locally while offline and
                // flush them on reconnect. That's why RecordedAtUtc is set by the
                // device, not the server.
                Log.Warn(Code, $"publish failed: {ex.Message}");
            }
        }

        await ShutdownGracefullyAsync();
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client.IsConnected) return;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_mqtt.Host, _mqtt.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            // The client id must be unique on the broker. If a second connection
            // uses the same id, the broker kicks the first one off.
            .WithClientId($"device-{Code}")
            // Keep-alive: if the broker hears nothing (not even a PING) for
            // 1.5 x this, it considers the client dead and fires the Last Will.
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
            // Persistent session: after a short disconnect, the broker remembers
            // this client's unacknowledged QoS 1 messages instead of starting fresh.
            .WithCleanStart(false)
            .WithSessionExpiryInterval(3600)
            // Last Will & Testament: registered NOW, published BY THE BROKER only if
            // this client disappears without a clean DISCONNECT (crash, power loss,
            // dead network). Retained, so anyone subscribing later still sees "offline".
            .WithWillTopic(MqttTopics.Status(Code))
            .WithWillPayload(DeviceConnectionStatus.Offline)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        // Exponential backoff with jitter. Retrying every 100ms against a broker
        // that's down just adds load to it once it comes back.
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _client.ConnectAsync(options, ct);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn(Code, $"connect failed ({ex.Message}), retrying in {delay.TotalSeconds:0}s");
                await Task.Delay(delay + TimeSpan.FromMilliseconds(_random.Next(0, 500)), ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        // Retained "online" replaces the retained "offline" from any earlier LWT,
        // so the status topic always holds the device's latest known state.
        await PublishAsync(MqttTopics.Status(Code), DeviceConnectionStatus.Online,
            MqttQualityOfServiceLevel.AtLeastOnce, retain: true, ct);
        Log.Info(Code, "connected");
    }

    private async Task PublishReadingAsync(CancellationToken ct)
    {
        NextTemperature();
        _latitude += (_random.NextDouble() - 0.5) * 0.001;
        _longitude += (_random.NextDouble() - 0.5) * 0.001;

        var reading = new DeviceTelemetry(
            MessageId: Guid.NewGuid(),
            DeviceCode: Code,
            BatchId: _binding.BatchId,
            TemperatureC: Math.Round((decimal)_temperature, 2),
            HumidityPercent: Math.Round((decimal)(45 + _random.NextDouble() * 10), 1),
            Latitude: Math.Round(_latitude, 6),
            Longitude: Math.Round(_longitude, 6),
            RecordedAtUtc: DateTime.UtcNow);

        // QoS 1 (at least once): the broker must PUBACK, or the client resends.
        // A reading must never be silently lost, and a rare duplicate is fine
        // because it's deduplicated by MessageId later.
        await PublishAsync(MqttTopics.Telemetry(Code), JsonSerializer.Serialize(reading, ContractJson.Options),
            MqttQualityOfServiceLevel.AtLeastOnce, retain: false, ct);

        var marker = _excursionTicksLeft > 0 ? "  <- EXCURSION" : "";
        Log.Info(Code, $"{reading.TemperatureC,6:0.00} °C{marker}");
    }

    private Task PublishHeartbeatAsync(CancellationToken ct)
    {
        _battery = Math.Max(0, _battery - _random.Next(0, 2));
        var heartbeat = new DeviceHeartbeat(Code, _battery, _random.Next(60, 101), DateTime.UtcNow);

        // QoS 0 (at most once, fire-and-forget): a lost heartbeat doesn't matter
        // because the next one arrives in a few seconds anyway.
        return PublishAsync(MqttTopics.Heartbeat(Code), JsonSerializer.Serialize(heartbeat, ContractJson.Options),
            MqttQualityOfServiceLevel.AtMostOnce, retain: false, ct);
    }

    /// <summary>Mean-reverting random walk around 5 °C, with occasional excursions
    /// that climb well past the 8 °C limit, so later phases have breaches to detect.</summary>
    private void NextTemperature()
    {
        if (_excursionTicksLeft == 0 && _random.NextDouble() * 100 < _sim.ExcursionChancePercent)
            _excursionTicksLeft = _random.Next(5, 15);

        if (_excursionTicksLeft > 0)
        {
            _temperature += 0.3 + _random.NextDouble() * 0.6;
            _excursionTicksLeft--;
        }
        else
        {
            _temperature += (SafeCenterC - _temperature) * 0.2 + (_random.NextDouble() - 0.5) * 0.3;
        }
    }

    private Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos, bool retain, CancellationToken ct)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithContentType("application/json")
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(retain)
            .Build();
        return _client.PublishAsync(message, ct);
    }

    /// <summary>
    /// Ctrl+C path. We announce "offline" ourselves, then send a clean DISCONNECT,
    /// and the broker DISCARDS the Last Will. Kill the process instead (close the
    /// window) and there's no DISCONNECT: the OS still closes the socket, so the
    /// broker notices at once and publishes the Will immediately. The keep-alive
    /// timeout (1.5 x 15s = about 22s) only matters when the link dies SILENTLY:
    /// power loss, pulled cable, a truck in a tunnel. No FIN ever arrives, so
    /// only the missing PINGs reveal it.
    /// </summary>
    private async Task ShutdownGracefullyAsync()
    {
        if (!_client.IsConnected) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await PublishAsync(MqttTopics.Status(Code), DeviceConnectionStatus.Offline,
                MqttQualityOfServiceLevel.AtLeastOnce, retain: true, timeout.Token);
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(), timeout.Token);
            Log.Info(Code, "disconnected cleanly");
        }
        catch (Exception ex)
        {
            Log.Warn(Code, $"clean disconnect failed: {ex.Message}");
        }
    }
}
