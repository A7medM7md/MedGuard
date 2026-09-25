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
}

public record DeviceBinding(string DeviceCode, Guid BatchId);
