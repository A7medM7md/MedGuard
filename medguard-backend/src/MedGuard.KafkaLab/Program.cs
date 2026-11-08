using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

// A hands-on lab for Kafka fundamentals, not part of the pipeline.
// Every command prints what Kafka actually did (partition, offset, assignment),
// so you can SEE the concepts instead of taking them on faith.
//
//   dotnet run -- metadata
//   dotnet run -- produce <topic> <count> [keys=a,b,c | keys=none] [partitioner=murmur2|crc32]
//   dotnet run -- consume <topic> <group> <name> [commit=manual|auto|none] [crash-after=N] [delay-ms=N]
//   dotnet run -- fill-readings <count> [batches=1000] [temp=12.5] [dup=true] [unknown=N]
//                                  load-test backlog / batch-path failure cases (ADR 0008)

Console.OutputEncoding = Encoding.UTF8;
const string Bootstrap = "localhost:9092";

var cmd = args.ElementAtOrDefault(0);
var opts = args.Where(a => a.Contains('=')).Select(a => a.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
var pos = args.Where(a => !a.Contains('=')).ToArray();

switch (cmd)
{
    case "metadata": Metadata(); break;
    case "produce": await Produce(pos[1], int.Parse(pos[2])); break;
    case "consume": Consume(pos[1], pos[2], pos[3]); break;
    case "fill-readings": await FillReadings(int.Parse(pos[1]), int.Parse(opts.GetValueOrDefault("batches", "1000")),
        opts.TryGetValue("temp", out var temp) ? decimal.Parse(temp, System.Globalization.CultureInfo.InvariantCulture) : null,
        opts.GetValueOrDefault("dup") == "true", int.Parse(opts.GetValueOrDefault("unknown", "0"))); break;
    default:
        Console.WriteLine("usage: metadata | produce <topic> <count> [keys=..] [partitioner=..] | consume <topic> <group> <name> [commit=..] [crash-after=N] [delay-ms=N]");
        break;
}

// The first thing every client does: ask the bootstrap server "who are the
// brokers and where do partitions live?" Every later connection goes to the
// ADVERTISED addresses in this answer, not to the bootstrap address.
void Metadata()
{
    using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Bootstrap }).Build();
    var md = admin.GetMetadata(TimeSpan.FromSeconds(10));

    Console.WriteLine($"Bootstrapped via {Bootstrap}. The cluster says its brokers are:");
    foreach (var b in md.Brokers)
        Console.WriteLine($"  broker {b.BrokerId} -> {b.Host}:{b.Port}   <- the address clients will actually use");

    Console.WriteLine("\nTopics:");
    foreach (var t in md.Topics.Where(t => !t.Topic.StartsWith("__")).OrderBy(t => t.Topic))
        Console.WriteLine($"  {t.Topic,-22} partitions={t.Partitions.Count}  leaders=[{string.Join(",", t.Partitions.Select(p => p.Leader))}]");
}

async Task Produce(string topic, int count)
{
    var keys = opts.GetValueOrDefault("keys", "batch-A,batch-B,batch-C");
    var keyList = keys == "none" ? null : keys.Split(',');

    var config = new ProducerConfig
    {
        BootstrapServers = Bootstrap,
        // acks=all: the leader replies only after every in-sync replica has the
        // write. With 1 broker that's just the leader, but the setting is what
        // makes data survive a broker crash in a real 3-node cluster.
        Acks = Acks.All,
        // Idempotent producer: the broker de-duplicates the producer's own
        // internal retries, so a network blip can't write a message twice.
        EnableIdempotence = true,
        // Wait up to 5ms to group messages into one request (a batch). This trades
        // a little latency for much higher throughput under load.
        LingerMs = 5,
        // How key -> partition is computed. librdkafka (.NET, Go, Python) defaults
        // to CRC32; Java clients use murmur2. The same key lands on DIFFERENT
        // partitions depending on the client, unless you pick murmur2 here.
        Partitioner = opts.GetValueOrDefault("partitioner") == "crc32"
            ? Partitioner.ConsistentRandom
            : Partitioner.Murmur2Random,
    };

    using var producer = new ProducerBuilder<string?, string>(config).Build();
    for (var i = 1; i <= count; i++)
    {
        var key = keyList?[(i - 1) % keyList.Length];
        var result = await producer.ProduceAsync(topic, new Message<string?, string>
        {
            Key = key,
            Value = $"{{\"n\":{i},\"key\":\"{key}\",\"at\":\"{DateTime.UtcNow:HH:mm:ss.fff}\"}}",
        });
        Console.WriteLine($"  key={key ?? "(null)",-8} -> partition {result.Partition.Value}, offset {result.Offset.Value}");
    }
}

void Consume(string topic, string group, string name)
{
    var commit = opts.GetValueOrDefault("commit", "manual");
    var crashAfter = int.Parse(opts.GetValueOrDefault("crash-after", "0"));
    var delayMs = int.Parse(opts.GetValueOrDefault("delay-ms", "0"));
    var idleExitSec = int.Parse(opts.GetValueOrDefault("idle-exit", "0"));

    var config = new ConsumerConfig
    {
        BootstrapServers = Bootstrap,
        // Consumers with the same GroupId SHARE the partitions of a topic (each
        // partition goes to exactly one member). Different groups each get ALL
        // messages, independently.
        GroupId = group,
        ClientId = name,
        // Only used when the group has NO committed offset yet for a partition:
        // Earliest = start from the beginning, Latest = only new messages.
        AutoOffsetReset = AutoOffsetReset.Earliest,
        // auto: a background timer commits the last polled offsets every 5s,
        //       whether or not you finished processing them.
        // manual: we commit explicitly, AFTER processing (at-least-once).
        EnableAutoCommit = commit == "auto",
        AutoCommitIntervalMs = 5000,
    };

    using var consumer = new ConsumerBuilder<string?, string>(config)
        .SetPartitionsAssignedHandler((_, parts) =>
            Log(name, $"ASSIGNED   partitions [{string.Join(",", parts.Select(p => p.Partition.Value))}]", ConsoleColor.Green))
        .SetPartitionsRevokedHandler((_, parts) =>
            Log(name, $"REVOKED    partitions [{string.Join(",", parts.Select(p => p.Partition.Value))}]", ConsoleColor.Yellow))
        .SetPartitionsLostHandler((_, parts) =>
            Log(name, $"LOST       partitions [{string.Join(",", parts.Select(p => p.Partition.Value))}]", ConsoleColor.Red))
        .Build();

    consumer.Subscribe(topic);
    Log(name, $"joined group '{group}' on '{topic}' (commit={commit})", ConsoleColor.Cyan);

    var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

    var processed = 0;
    var lastMessageAt = DateTime.UtcNow;
    try
    {
        while (!stop.IsCancellationRequested)
        {
            var r = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (r is null)
            {
                if (idleExitSec > 0 && DateTime.UtcNow - lastMessageAt > TimeSpan.FromSeconds(idleExitSec)) break;
                continue;
            }
            lastMessageAt = DateTime.UtcNow;

            if (delayMs > 0) Thread.Sleep(delayMs); // "processing"
            Log(name, $"p{r.Partition.Value} @offset {r.Offset.Value,-3} key={r.Message.Key,-8} {r.Message.Value}");

            if (commit == "manual")
                consumer.Commit(r); // commits offset+1 = "next message I want"

            if (crashAfter > 0 && ++processed >= crashAfter)
            {
                Log(name, $"CRASH after {processed} messages (no Close, no final commit)", ConsoleColor.Red);
                Environment.Exit(1); // skips Close(): the group isn't told we left
            }
        }
    }
    finally
    {
        // Close() commits (if auto) and sends LeaveGroup, so the group rebalances
        // IMMEDIATELY instead of waiting for session.timeout.ms to notice we're gone.
        consumer.Close();
        Log(name, "left the group cleanly", ConsoleColor.Cyan);
    }
}

static void Log(string who, string msg, ConsoleColor color = ConsoleColor.Gray)
{
    lock (Console.Out)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{who}] {msg}");
        Console.ResetColor();
    }
}

// Drain test (ADR 0008): put N valid readings on sensor-readings exactly as the
// bridge would (same key, same headers), with no MQTT involved. Then start
// workers and time how long they take to reach lag 0. That measures the
// CONSUMER's maximum throughput in isolation, without needing HiveMQ, the bridge
// and 1000 simulated devices (and their RAM) running at the same time.
// Batch ids match loadtest/seed-batches.sql: MD5("SIM-0001")... read as a GUID.
//
// Failure-case knobs, for checking the BATCH path's correctness:
//   temp=12.5   every reading at this temperature (12.5 = critical for a 2-8 range)
//   dup=true    send every message twice, like a redelivery
//   unknown=N   N extra readings for a batch that doesn't exist (they should be dead-lettered)
async Task FillReadings(int count, int batches, decimal? fixedTemp = null, bool duplicate = false, int unknown = 0)
{
    var batchIds = Enumerable.Range(1, batches)
        .Select(i => new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes($"SIM-{i:0000}"))))
        .ToArray();

    using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
    {
        BootstrapServers = Bootstrap,
        Acks = Acks.All,
        EnableIdempotence = true,
        // Throughput mode: wait a little to fill big batches, and compress them.
        LingerMs = 20,
        BatchSize = 1_000_000,
        CompressionType = CompressionType.Lz4,
    }).Build();

    var random = new Random(42);
    var started = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < count; i++)
    {
        var batchId = batchIds[i % batches];
        var reading = new MedGuard.Contracts.Devices.DeviceTelemetry(
            Guid.NewGuid(), $"SIM-{i % batches + 1:0000}", batchId,
            // Always safe (2-8 °C range): this measures the plain recording path.
            fixedTemp ?? Math.Round(4.0m + (decimal)random.NextDouble() * 2, 2), 50m, 30.05, 31.24, DateTime.UtcNow);

        var message = new Message<string, byte[]>
        {
            Key = batchId.ToString(),
            Value = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reading, MedGuard.Contracts.Devices.ContractJson.Options),
            Headers = new Headers
            {
                { MedGuard.Contracts.Kafka.KafkaHeaders.MessageId, Encoding.UTF8.GetBytes(reading.MessageId.ToString()) },
                { MedGuard.Contracts.Kafka.KafkaHeaders.Source, "kafkalab-fill"u8.ToArray() },
            },
        };

        Send(message);
        if (duplicate) Send(message);
    }

    for (var i = 0; i < unknown; i++)
    {
        var orphan = new MedGuard.Contracts.Devices.DeviceTelemetry(Guid.NewGuid(), "SIM-GHOST", Guid.NewGuid(),
            5m, 50m, null, null, DateTime.UtcNow);
        Send(new Message<string, byte[]>
        {
            Key = orphan.BatchId.ToString(),
            Value = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(orphan, MedGuard.Contracts.Devices.ContractJson.Options),
        });
    }
    producer.Flush(TimeSpan.FromSeconds(30));

    // Produce (not ProduceAsync): fire into librdkafka's queue, no Task per
    // message. When the local queue is full, wait for it to drain and retry.
    void Send(Message<string, byte[]> m)
    {
        while (true)
        {
            try { producer.Produce(MedGuard.Contracts.Kafka.KafkaTopics.SensorReadings, m); return; }
            catch (ProduceException<string, byte[]> e) when (e.Error.Code == ErrorCode.Local_QueueFull) { producer.Poll(TimeSpan.FromMilliseconds(50)); }
        }
    }
    Console.WriteLine($"filled {count} readings over {batches} batches in {started.Elapsed.TotalSeconds:0.0}s ({count / started.Elapsed.TotalSeconds:0}/s)");
}
