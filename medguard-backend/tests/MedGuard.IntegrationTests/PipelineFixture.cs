using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Contracts.Kafka;
using MedGuard.Domain.Entities;
using MedGuard.Domain.ValueObjects;
using MedGuard.Infrastructure;
using MedGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

namespace MedGuard.IntegrationTests;

/// <summary>
/// One real SQL Server and one real Kafka for the whole test run (ADR 0009).
/// Starting them takes ~20s, so they're shared, and tests isolate themselves by
/// creating their own batches (fresh ids) instead of wiping tables.
///
/// The images are the same ones docker-compose uses, so a dev machine that has
/// run the stack pulls nothing new.
/// </summary>
public sealed class PipelineFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:3.9.0").Build();

    public ServiceProvider Services { get; private set; } = default!;
    public string KafkaBootstrap => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _kafka.StartAsync());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MedGuardDb"] = _sql.GetConnectionString(),
            })
            .Build();

        // Exactly the registrations the worker and the API use.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ResponseHandler>();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        Services = services.BuildServiceProvider();

        // The real migrations, not EnsureCreated: the filtered unique index on
        // MessageId and the outbox table are part of what's under test.
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MedGuardDbContext>().Database.MigrateAsync();

        // Auto-create is off in production (docker-compose kafka-init), so create them here too.
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = KafkaBootstrap }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = KafkaTopics.SensorReadings, NumPartitions = 3, ReplicationFactor = 1 },
            new TopicSpecification { Name = KafkaTopics.SensorReadingsDlq, NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = KafkaTopics.BatchAlerts, NumPartitions = 1, ReplicationFactor = 1 },
        ]);
    }

    public async Task DisposeAsync()
    {
        // Null if InitializeAsync failed (e.g. Docker unavailable): don't mask that error with a NullReferenceException.
        if (Services is not null)
            await Services.DisposeAsync();
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    /// <summary>A fresh batch with a 2–8 °C range. Optionally moved to Delivered.</summary>
    public async Task<Batch> SeedBatchAsync(bool delivered = false)
    {
        var batch = new Batch($"IT-{Guid.NewGuid():N}"[..20], "Vaccine", "Maker", 100,
            DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow.AddYears(1), new TemperatureRange(2m, 8m));
        if (delivered)
        {
            batch.Ship("Factory", "Pharmacy", null);
            batch.MarkDelivered();
        }

        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<MedGuardDbContext>();
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    public IProducer<string, byte[]> CreateProducer() =>
        new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = KafkaBootstrap,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();

    /// <summary>Reads a topic from the beginning until <paramref name="until"/> is satisfied or the timeout passes.</summary>
    public List<ConsumeResult<string, byte[]>> ReadTopic(string topic, Func<List<ConsumeResult<string, byte[]>>, bool> until,
        TimeSpan? timeout = null)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = KafkaBootstrap,
            GroupId = $"it-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var seen = new List<ConsumeResult<string, byte[]>>();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline && !until(seen))
        {
            var r = consumer.Consume(TimeSpan.FromMilliseconds(250));
            if (r is not null) seen.Add(r);
        }
        consumer.Close();
        return seen;
    }
}

[CollectionDefinition(Name)]
public sealed class PipelineCollection : ICollectionFixture<PipelineFixture>
{
    public const string Name = "pipeline";
}
