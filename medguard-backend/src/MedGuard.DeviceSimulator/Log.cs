namespace MedGuard.DeviceSimulator;

/// <summary>Minimal console logger. Many devices write concurrently, so writes are serialized.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    /// <summary>Quiet mode drops per-reading Info lines; warnings still print (see SimulationOptions.Quiet).</summary>
    public static bool Quiet { get; set; }

    public static void Info(string device, string message)
    {
        if (!Quiet) Write(device, message, ConsoleColor.Gray);
    }

    public static void Warn(string device, string message) => Write(device, message, ConsoleColor.Yellow);

    public static void Summary(string message) => Write("sim", message, ConsoleColor.Cyan);

    private static void Write(string device, string message, ConsoleColor color)
    {
        lock (Gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{device}] {message}");
            Console.ResetColor();
        }
    }
}

/// <summary>Fleet-wide counters, updated by every device concurrently, hence Interlocked.</summary>
internal static class FleetStats
{
    public static long Published, Failed, Connected;

    /// <summary>Publish latency: time until the broker's PUBACK (QoS 1).</summary>
    private static long _publishTicks, _publishCount;

    public static void AddPublish(TimeSpan elapsed)
    {
        Interlocked.Increment(ref Published);
        Interlocked.Add(ref _publishTicks, elapsed.Ticks);
        Interlocked.Increment(ref _publishCount);
    }

    public static async Task ReportAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        long last = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var published = Interlocked.Read(ref Published);
                var count = Interlocked.Exchange(ref _publishCount, 0);
                var ticks = Interlocked.Exchange(ref _publishTicks, 0);
                var avgAck = count == 0 ? "-" : $"{TimeSpan.FromTicks(ticks / count).TotalMilliseconds:0.0}ms";

                Log.Summary($"connected={Interlocked.Read(ref Connected)} published={published} " +
                            $"rate={(published - last) / 5.0:0.0}/s failed={Interlocked.Read(ref Failed)} avg-puback={avgAck}");
                last = published;
            }
        }
        catch (OperationCanceledException) { }
    }
}
