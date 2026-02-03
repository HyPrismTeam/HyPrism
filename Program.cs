using Avalonia;
using HyPrism.Services.Core;
using HyPrism.UI;

using Avalonia.ReactiveUI;
using Serilog;

namespace HyPrism;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Initialize Logger
        var appDir = UtilityService.GetEffectiveAppDir();
        var logsDir = Path.Combine(appDir, "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: Path.Combine(logsDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 7
            )
            .CreateLogger();

        try
        {
            Logger.Info("Boot", "Starting HyPrism...");
            Logger.Info("Boot", $"App Directory: {appDir}");

            // Print ASCII Logo
            try
            {
                Console.WriteLine("""

 .-..-.      .---.       _                
 : :; :      : .; :     :_;               
 :    :.-..-.:  _.'.--. .-. .--. ,-.,-.,-.
 : :: :: :; :: :   : ..': :`._-.': ,. ,. :
 :_;:_;`._. ;:_;   :_;  :_;`.__.':_;:_;:_;
        .-. :                             
        `._.'                     launcher

""");
        }
        catch { /* Ignore if console is not available */ }

        // Check for wrapper mode flag
        if (args.Contains("--wrapper"))
        {
            // In wrapper mode, launch the wrapper UI
            // This is used by Flatpak/AppImage to manage the installation of the actual HyPrism binary
            Logger.Info("Wrapper", "Running in wrapper mode");
            // The wrapper UI will use WrapperGetStatus, WrapperInstallLatest, WrapperLaunch methods
        }
        
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed unexpectedly");
            Logger.Error("Crash", $"Application crashed: {ex.Message}");
            Console.WriteLine(ex.ToString());
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .With(new SkiaOptions { UseOpacitySaveLayer = true })
            .LogToTrace();
            
}
