using Serilog;

namespace HyPrism.Services.Core;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly Queue<string> _logBuffer = new();
    private const int MaxLogEntries = 100;
    
    public static void Info(string category, string message)
    {
        Log.ForContext("SourceContext", category).Information(message);
        WriteToConsole("INF", category, message, ConsoleColor.Gray);
        AddToBuffer("INF", category, message);
    }
    
    public static void Success(string category, string message)
    {
        Log.ForContext("SourceContext", category).Information($"SUCCESS: {message}");
        WriteToConsole("SUC", category, message, ConsoleColor.Green);
        AddToBuffer("SUC", category, message);
    }
    
    public static void Warning(string category, string message)
    {
        Log.ForContext("SourceContext", category).Warning(message);
        WriteToConsole("WRN", category, message, ConsoleColor.Yellow);
        AddToBuffer("WRN", category, message);
    }
    
    public static void Error(string category, string message)
    {
        Log.ForContext("SourceContext", category).Error(message);
        WriteToConsole("ERR", category, message, ConsoleColor.Red);
        AddToBuffer("ERR", category, message);
    }
    
    public static void Debug(string category, string message)
    {
#if DEBUG
        Log.ForContext("SourceContext", category).Debug(message);
        WriteToConsole("DBG", category, message, ConsoleColor.DarkGray);
        AddToBuffer("DBG", category, message);
#endif
    }
    
    public static List<string> GetRecentLogs(int count = 10)
    {
        lock (_lock)
        {
            var entries = _logBuffer.ToArray();
            var start = Math.Max(0, entries.Length - count);
            var result = new List<string>();
            for (int i = start; i < entries.Length; i++)
            {
                result.Add(entries[i]);
            }
            return result;
        }
    }
    
    private static void WriteToConsole(string level, string category, string message, ConsoleColor color)
    {
        lock (_lock)
        {
            try 
            {
                if (Console.IsOutputRedirected) return;

                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                
                Console.Write($"{timestamp} ");
                
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write(level);
                Console.ForegroundColor = originalColor;
                
                Console.WriteLine($" {category}: {message}");
            }
            catch { /* Ignore */ }
        }
    }

    private static void AddToBuffer(string level, string category, string message)
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"{timestamp} | {level} | {category} | {message}";
            
            _logBuffer.Enqueue(logEntry);
            while (_logBuffer.Count > MaxLogEntries)
            {
                _logBuffer.Dequeue();
            }
        }
    }

    public static void Progress(string category, int percent, string message)
    {
        lock (_lock)
        {
            try {
                if (!Console.IsOutputRedirected)
                {
                    Console.Write($"\r[{category}] {message,-40} [{ProgressBar(percent, 20)}] {percent,3}%");
                    if (percent >= 100)
                    {
                        Console.WriteLine();
                    }
                }
            }
            catch { /* Ignore */ }
        }
    }

    
    private static string ProgressBar(int percent, int width)
    {
        int filled = (int)(percent / 100.0 * width);
        int empty = width - filled;
        return new string('=', filled) + new string('-', empty);
    }
}
