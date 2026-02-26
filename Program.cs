using EmptyFlow.SciterAPI;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Ipc;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.User;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Runtime;
using System.Text;

namespace HyPrism;

class Program
{
    static async Task Main(string[] args)
    {
        // Memory optimisation
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GCSettings.LatencyMode = GCLatencyMode.Interactive;

        // Save original Console.Out before anything (Sciter) may replace it.
        // Logger.WriteToConsole uses _originalOut so our log output continues
        // to appear in the terminal even after we redirect Console.Out.
        Logger.CaptureOriginalConsole();

        // ── Logger initialisation ─────────────────────────────────────────────
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
        catch { /* ignore */ }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: logFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 20)
            .CreateLogger();

        try
        {
            Logger.Info("Boot", "Starting HyPrism (Sciter)...");
            Logger.Info("Boot", $"App Directory: {appDir}");

            // ── DI container ─────────────────────────────────────────────────
            var services = Bootstrapper.Initialize();
            await Bootstrapper.InitializeAsync(services);

            // ── Sciter bootstrap (blocks until window is closed) ─────────────
            SciterBootstrap(services);
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

    // ─────────────────────────────────────────────────────────────────────────
    private static void SciterBootstrap(IServiceProvider services)
    {
        var wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");

        // SciterAPI expects the native library (libsciter.so / sciter.dll / libsciter.dylib)
        // to reside next to the application executable.
        var sciterLibDir = AppDomain.CurrentDomain.BaseDirectory;

        // Redirect Console.Out/Error so Sciter's own diagnostic output goes
        // through our structured Logger instead of raw stdout.
        var sciterLog = new SciterStdoutRedirector();
        Console.SetOut(sciterLog);
        Console.SetError(sciterLog);

        // ── Create Sciter host ────────────────────────────────────────────────
        var host = new SciterAPIHost(sciterLibDir);

        host.CreateMainWindow(
            1280, 800,
            enableDebug: IsDebugBuild(),
            enableFeature: true);

        Logger.Info("Boot", "Sciter window created");

        // ── Attach IPC bridge BEFORE registering handlers ─────────────────────
        var bridge = services.GetRequiredService<SciterIpcBridge>();
        bridge.Attach(host, host.MainWindow);

        // ── Register IPC handlers ─────────────────────────────────────────────
        var ipcService = services.GetRequiredService<IpcService>();
        ipcService.RegisterAll();

        // ── Run instance migrations ───────────────────────────────────────────
        var instanceService = services.GetRequiredService<IInstanceService>();
        instanceService.MigrateLegacyData();
        instanceService.MigrateVersionFoldersToIdFolders();

        var profileManagementService = services.GetRequiredService<IProfileManagementService>();
        profileManagementService.InitializeProfileModsSymlink();

        // ── Set window properties ─────────────────────────────────────────────
        host.SetWindowCaption(host.MainWindow, "HyPrism");
        host.SetWindowMinimizable(host.MainWindow, true);
        host.SetWindowResizable(host.MainWindow, true);

        // ── Remove OS window decorations (uses our custom titlebar) ──────────────
        // SciterWindowFrameType.Solid = frameless solid-background window
        // (removes GTK/X11 native title bar; app-region drag handled via JS)
        host.SetWindowFrameType(host.MainWindow, SciterWindowFrameType.Solid);

        // ── Load the frontend HTML ─────────────────────────────────────────────
        var indexPath = Path.Combine(wwwroot, "index.html");
        if (!File.Exists(indexPath))
        {
            Logger.Error("Boot", $"Frontend not found at {indexPath} — run 'dotnet build' first");
            return;
        }

        host.LoadFile($"file://{indexPath.Replace('\\', '/')}");

        // Trigger launcher update check after the window has loaded
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                var updateService = services.GetRequiredService<HyPrism.Services.Core.App.IUpdateService>();
                await updateService.CheckForLauncherUpdatesAsync();
            }
            catch (Exception ex)
            {
                Logger.Warning("Update", $"Startup update check failed: {ex.Message}");
            }
        });

        Logger.Success("Boot", "Sciter message loop starting...");

        // host.Process() blocks until the window is closed
        host.Process();

        Logger.Info("Boot", "Sciter window closed, exiting");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
