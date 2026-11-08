using Microsoft.EntityFrameworkCore;
using Confluent.Kafka;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Infrastructure;
using MedGuard.Infrastructure.Persistence;
using MedGuard.Monitoring.Worker;
using MedGuard.ServiceDefaults;
using Microsoft.Extensions.Options;

// Run several instances side by side; each needs its own InstanceId:
//   dotnet run -- --Monitoring:InstanceId=2
// Health: http://localhost:{5100 + InstanceId}/health/live and /health/ready (or pass --urls).
var builder = WebApplication.CreateBuilder(args);
var instanceId = builder.Configuration["Monitoring:InstanceId"] ?? "1";
builder.AddServiceDefaults("medguard-worker", instanceId);

if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls($"http://localhost:{5100 + (int.TryParse(instanceId, out var n) ? n : 0)}");

builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MonitoringOptions>>().Value);

// The same Application + Infrastructure layers the API uses: the domain rules
// for a reading are identical whether it arrives over HTTP or Kafka.
builder.Services.AddScoped<ResponseHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IProducer<string, byte[]>>(sp =>
{
    var options = sp.GetRequiredService<MonitoringOptions>();
    return new ProducerBuilder<string, byte[]>(new ProducerConfig
    {
        BootstrapServers = options.KafkaBootstrapServers,
        ClientId = $"monitoring-{options.InstanceId}-dlq",
        Acks = Acks.All,
        EnableIdempotence = true,
        Partitioner = Partitioner.Murmur2Random,
        // The DLQ producer sends ONE message and waits for it, so there's nothing
        // to batch: don't linger, and disable Nagle's algorithm, which otherwise
        // holds small TCP packets back hoping to coalesce them (measured: ~59ms
        // per DLQ produce with defaults).
        LingerMs = 0,
        SocketNagleDisable = true,
    }).Build();
});
builder.Services.AddSingleton<WorkerHealth>();
builder.Services.AddSingleton<TelemetryMessageHandler>();
builder.Services.AddHostedService<TelemetryConsumer>();

var health = builder.Services.AddHealthChecks()
    .AddCheck<ConsumerLoopHealthCheck>("consume-loop", tags: [ServiceDefaultsExtensions.Live, ServiceDefaultsExtensions.Ready])
    .AddDbContextCheck<MedGuardDbContext>("sql", tags: [ServiceDefaultsExtensions.Ready],
        // A plain query, not the default CanConnect: on SQL Server, CanConnect retries
        // a failed login for a full minute (it waits for databases being created).
        customTestQuery: (db, ct) => db.Database.SqlQueryRaw<int>("SELECT 1 AS Value").AnyAsync(ct));

// Drains OutboxMessages into Kafka (ADR 0006). It also drains rows written by the
// HTTP endpoint, since both paths share ColdChainMonitoringService.RecordAsync.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
if (builder.Configuration.GetValue("Outbox:Enabled", true))
{
    // One instance, two roles: the hosted service, and the circuit state the health check reads.
    builder.Services.AddSingleton<OutboxDispatcher>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
    health.AddCheck<OutboxHealthCheck>("outbox", tags: [ServiceDefaultsExtensions.Ready]);
}

var app = builder.Build();
app.MapHealthEndpoints();
app.Run();
