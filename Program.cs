using HyPrism.Hosts;
using HyPrism.Services.Core.Infrastructure;

using Serilog;
using System.Runtime;

namespace HyPrism;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Memory optimization
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GCSettings.LatencyMode = GCLatencyMode.Interactive;

        // Initialize Logger
        var appDir = UtilityService.GetEffectiveAppDir();
        var logsDir = Path.Combine(appDir, "Logs");
        Directory.CreateDirectory(logsDir);

        var logFileName = $"{DateTime.Now:dd-MM-yyyy_HH-mm-ss}.log";
        var logFilePath = Path.Combine(logsDir, logFileName);

        try
        {
            File.WriteAllText(logFilePath, """
 .-..-.      .---.       _                
 : :; :      : .; :     :_;               
 :    :.-..-.:  _.'.--. .-. .--. ,-.,-.,-.
 : :: :: :; :: :   : ..': :`._-.': ,. ,. :
 :_;:_;`._. ;:_;   :_;  :_;`.__.':_;:_;:_;
        .-. :                             
        `._.'                     launcher

""" + Environment.NewLine);
        }
        catch { /* Ignore */ }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: logFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 20
            )
            .CreateLogger();

        var runtimeHost = RuntimeHostFactory.Create(args);

        try
        {
            Logger.Info("Boot", $"Starting HyPrism ({runtimeHost.Name})...");
            Logger.Info("Boot", $"App Directory: {appDir}");
            Logger.Info("Boot", $"Runtime host: {runtimeHost.Id}");

            // Initialize DI container
            var services = Bootstrapper.Initialize();
            
            // Perform async initialization (fetch CurseForge key if needed)
            await Bootstrapper.InitializeAsync(services);

            await runtimeHost.RunAsync(services);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed unexpectedly");
            Logger.Error("Crash", $"Application crashed: {ex.Message}");
            Console.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
