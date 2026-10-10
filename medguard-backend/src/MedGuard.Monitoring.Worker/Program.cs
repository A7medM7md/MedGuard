using Confluent.Kafka;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Infrastructure;
using MedGuard.Monitoring.Worker;
using Microsoft.Extensions.Options;

// Run several instances side by side; each needs its own InstanceId:
//   dotnet run -- --Monitoring:InstanceId=2
var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<TelemetryMessageHandler>();
builder.Services.AddHostedService<TelemetryConsumer>();

builder.Build().Run();
