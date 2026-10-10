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
