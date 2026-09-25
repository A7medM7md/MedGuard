using System.Security.Cryptography;
using System.Text;
using MedGuard.DeviceSimulator;
using Microsoft.Extensions.Configuration;

// Usage (from src/MedGuard.DeviceSimulator):
//   dotnet run                                        5 devices, a reading every 2s
//   dotnet run -- --Simulation:DeviceCount=50 --Simulation:IntervalSeconds=0.5
//   dotnet run -- watch                               print everything under medguard/devices/#
//   dotnet run -- watch "medguard/devices/+/status"   only statuses (try other filters!)

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

var bindings = sim.Devices.Count > 0
    ? sim.Devices
    : Enumerable.Range(1, sim.DeviceCount)
        .Select(i => $"SIM-{i:0000}")
        .Select(code => new DeviceBinding(code, StableGuid(code)))
        .ToList();

Console.WriteLine($"Starting {bindings.Count} device(s) -> {mqtt.Host}:{mqtt.Port}, " +
                  $"a reading every {sim.IntervalSeconds}s. Ctrl+C = clean shutdown, close window = crash (LWT).\n");

var devices = bindings.Select(b => new SimulatedDevice(b, mqtt, sim)).ToList();
await Task.WhenAll(devices.Select(d => d.RunAsync(cts.Token)));

// Same device code gives the same batch id on every run, so restarting the
// simulator keeps each device's readings on the same batch (and later, the
// same Kafka partition).
static Guid StableGuid(string seed) => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
