namespace MedGuard.DeviceSimulator;

public class MqttOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
}

public class SimulationOptions
{
    /// <summary>How many fake devices to start when <see cref="Devices"/> is empty.</summary>
    public int DeviceCount { get; set; } = 5;

    public double IntervalSeconds { get; set; } = 2;

    /// <summary>Send a heartbeat every N readings.</summary>
    public int HeartbeatEvery { get; set; } = 5;

    /// <summary>Chance per reading (0-100) that a temperature excursion starts,
    /// e.g. a fridge door left open or a power cut.</summary>
    public double ExcursionChancePercent { get; set; } = 3;

    /// <summary>Real device/batch pairs from the database. When empty, fake
    /// devices SIM-0001..SIM-{DeviceCount} are generated instead.</summary>
    public List<DeviceBinding> Devices { get; set; } = [];

    /// <summary>
    /// How many batches the fake devices share. 0 (default) = one batch per device.
    /// Set it low (say 3) to make HOT KEYS: Kafka partitions by BatchId, so 1000
    /// devices on 3 batches land on at most 3 partitions, however many partitions
    /// or consumers exist. Seeding SIM-0001..SIM-{DeviceCount} covers both setups
    /// (loadtest/seed-batches.sql).
    /// </summary>
    public int BatchCount { get; set; }

    /// <summary>
    /// No line per reading; one summary line every 5s instead. At 1000 devices
    /// x 1/s, per-reading console output costs more than the MQTT publishing
    /// does. The load generator must never be the bottleneck being measured.
    /// </summary>
    public bool Quiet { get; set; }
}

public record DeviceBinding(string DeviceCode, Guid BatchId);
