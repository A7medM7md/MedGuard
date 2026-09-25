namespace MedGuard.DeviceSimulator;

/// <summary>Minimal console logger. Many devices write concurrently, so writes are serialized.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string device, string message) => Write(device, message, ConsoleColor.Gray);
    public static void Warn(string device, string message) => Write(device, message, ConsoleColor.Yellow);

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
