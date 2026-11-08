using System.Security.Cryptography;
using System.Text;
using MedGuard.DeviceSimulator;
using Microsoft.Extensions.Configuration;

// Usage (from src/MedGuard.DeviceSimulator):
//   dotnet run                                        5 devices, a reading every 2s
//   dotnet run -- --Simulation:DeviceCount=50 --Simulation:IntervalSeconds=0.5
//   dotnet run -- watch                               print everything under medguard/devices/#
//   dotnet run -- watch "medguard/devices/+/status"   only statuses (try other filters!)
//   dotnet run -- publish <topic> '<payload>'         send one hand-written message

Console.OutputEncoding = Encoding.UTF8; // for "°C"

var mode = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "run";
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddCommandLine(args.Where(a => a.StartsWith("--")).ToArray())
    .Build();

var mqtt = config.GetSection("Mqtt").Get<MqttOptions>() ?? new MqttOptions();
var sim = config.GetSection("Simulation").Get<SimulationOptions>() ?? new SimulationOptions();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // don't kill the process; let every device disconnect cleanly
    cts.Cancel();
};

if (mode == "watch")
{
    var filter = args.Where(a => !a.StartsWith("--")).Skip(1).FirstOrDefault() ?? Watcher.DefaultFilter;
    await Watcher.RunAsync(mqtt, filter, cts.Token);
    return;
}

if (mode == "publish")
{
    var positional = args.Where(a => !a.StartsWith("--")).ToArray();
    await RawPublisher.RunAsync(mqtt, positional[1], positional[2], cts.Token);
    return;
}

// Device i reports for batch SIM-{((i-1) % batchCount) + 1}. With the default
// (one batch per device) that's its own code, exactly as before.
var batchCount = sim.BatchCount > 0 ? sim.BatchCount : sim.DeviceCount;
var bindings = sim.Devices.Count > 0
    ? sim.Devices
    : Enumerable.Range(1, sim.DeviceCount)
        .Select(i => new DeviceBinding($"SIM-{i:0000}", StableGuid($"SIM-{(i - 1) % batchCount + 1:0000}")))
        .ToList();

Log.Quiet = sim.Quiet;
Console.WriteLine($"Starting {bindings.Count} device(s) on {bindings.Select(b => b.BatchId).Distinct().Count()} batch(es) " +
                  $"-> {mqtt.Host}:{mqtt.Port}, a reading every {sim.IntervalSeconds}s. " +
                  "Ctrl+C = clean shutdown, close window = crash (LWT).\n");

var devices = bindings.Select(b => new SimulatedDevice(b, mqtt, sim)).ToList();
var report = FleetStats.ReportAsync(cts.Token);
await Task.WhenAll(devices.Select(d => d.RunAsync(cts.Token)));
await report;

// Same seed gives the same batch id on every run, so restarting the simulator
// keeps each device's readings on the same batch (and the same Kafka partition).
// It's the same value as SQL Server's CAST(HASHBYTES('MD5', seed) AS uniqueidentifier)
// (both read the 16 bytes in the same mixed-endian layout), which is how
// loadtest/seed-batches.sql creates matching batches.
static Guid StableGuid(string seed) => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
