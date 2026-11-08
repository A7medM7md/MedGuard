using MedGuard.IngestionBridge;
using MedGuard.ServiceDefaults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// Run several instances side by side; each needs its own InstanceId:
//   dotnet run -- --Bridge:InstanceId=2
// Health: http://localhost:{5200 + InstanceId}/health/ready (or pass --urls).
var builder = WebApplication.CreateBuilder(args);
var instanceId = builder.Configuration["Bridge:InstanceId"] ?? "1";
builder.AddServiceDefaults("medguard-bridge", instanceId);

if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls($"http://localhost:{5200 + (int.TryParse(instanceId, out var n) ? n : 0)}");

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
// One instance, two roles: the hosted service, and the state the health check reads.
builder.Services.AddSingleton<MqttToKafkaBridge>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttToKafkaBridge>());
builder.Services.AddHealthChecks()
    .AddCheck<BridgeHealthCheck>("bridge", tags: [ServiceDefaultsExtensions.Ready]);

var app = builder.Build();
app.MapHealthEndpoints();
app.Run();

/// <summary>
/// READY when connected to the broker and, if messages are waiting on Kafka,
/// acks are still coming back. Messages waiting with no ack for 30s means Kafka
/// is down or stuck; the backlog is then piling up in the broker (ADR 0008, finding 4).
/// </summary>
internal sealed class BridgeHealthCheck(MqttToKafkaBridge bridge) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object> { ["mqttConnected"] = bridge.IsMqttConnected, ["inFlight"] = bridge.InFlight };
        if (!bridge.IsMqttConnected)
            return Task.FromResult(HealthCheckResult.Unhealthy("not connected to the MQTT broker", data: data));

        if (bridge.InFlight > 0 && (bridge.LastAckUtc is not { } last || DateTime.UtcNow - last > TimeSpan.FromSeconds(30)))
            return Task.FromResult(HealthCheckResult.Degraded("messages waiting on Kafka with no ack for 30s", data: data));

        return Task.FromResult(HealthCheckResult.Healthy(data: data));
    }
}
