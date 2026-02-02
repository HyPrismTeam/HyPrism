using System;
using System.Collections.Generic;

namespace HyPrism.Backend;

public enum AppLogLevel
{
    Debug = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4,
    None = 5  // Completely silent
}

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly Queue<string> _logBuffer = new();
    public static string? LogFile { get; set; } = null;
    private const int MaxLogEntries = 100;
    
    // Default: only show Success, Warning, and Error messages
    public static AppLogLevel MinimumLevel { get; set; } = AppLogLevel.Success;
    
    public static void Info(string category, string message)
    {
        if (MinimumLevel <= AppLogLevel.Info)
            WriteLog("INFO", category, message, ConsoleColor.White);
        else
            BufferOnly("INFO", category, message);
    }
    
    public static void Success(string category, string message)
    {
        if (MinimumLevel <= AppLogLevel.Success)
            WriteLog("OK", category, message, ConsoleColor.Green);
        else
            BufferOnly("OK", category, message);
    }
    
    public static void Warning(string category, string message)
    {
        if (MinimumLevel <= AppLogLevel.Warning)
            WriteLog("WARN", category, message, ConsoleColor.Yellow);
        else
            BufferOnly("WARN", category, message);
    }
    
    public static void Error(string category, string message)
    {
        if (MinimumLevel <= AppLogLevel.Error)
            WriteLog("ERR", category, message, ConsoleColor.Red);
        else
            BufferOnly("ERR", category, message);
    }
    
    public static void Debug(string category, string message)
    {
#if DEBUG
        if (MinimumLevel <= AppLogLevel.Debug)
            WriteLog("DBG", category, message, ConsoleColor.Gray);
        else
            BufferOnly("DBG", category, message);
#endif
    }
    
    // Add to buffer without printing (for log retrieval later)
    private static void BufferOnly(string level, string category, string message)
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
            
            WriteToFile(logEntry);
        }
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
    
    public static void Progress(string category, int percent, string message)
    {
        lock (_lock)
        {
            Console.Write($"\r[{category}] {message.PadRight(40)} [{ProgressBar(percent, 20)}] {percent,3}%");
            if (percent >= 100)
            {
                Console.WriteLine();
            }
        }
    }
    
    private static void WriteLog(string level, string category, string message, ConsoleColor color)
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var icon = level switch
            {
                "OK" => "✔",
                "WARN" => "⚠",
                "ERR" => "✖",
                _ => "•"
            };

            var logEntry = $"{timestamp} | {level} | {category} | {message}";
            
            // Add to buffer
            _logBuffer.Enqueue(logEntry);
            while (_logBuffer.Count > MaxLogEntries)
            {
                _logBuffer.Dequeue();
            }
            
            WriteToFile(logEntry);
            
            var originalColor = Console.ForegroundColor;
            
            Console.Write($"{timestamp}  ");
            Console.ForegroundColor = color;
            Console.Write($"{icon} {level}");
            Console.ForegroundColor = originalColor;
            Console.WriteLine($"  {category}: {message}");
        }
    }
    
    private static void WriteToFile(string logEntry)
    {
        if (string.IsNullOrWhiteSpace(LogFile))
            return;

        try
        {
            var directory = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(LogFile, logEntry + Environment.NewLine);
        }
        catch
        {
            // Intentionally swallow exceptions to avoid logger crashes
            // You may want to surface this in DEBUG builds
        }
    }

    
    private static string ProgressBar(int percent, int width)
    {
        int filled = (int)((percent / 100.0) * width);
        int empty = width - filled;
        return new string('=', filled) + new string('-', empty);
    }
}
