using MedGuard.IngestionBridge;

// Run several instances side by side; each needs its own InstanceId:
//   dotnet run -- --Bridge:InstanceId=2
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
builder.Services.AddHostedService<MqttToKafkaBridge>();

builder.Build().Run();
