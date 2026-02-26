using EmptyFlow.SciterAPI;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Ipc;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.User;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;

namespace HyPrism;

class Program
{
    // ── libc helpers for environment variable injection ───────────────────────
    // Environment.SetEnvironmentVariable() is too late for GTK: the native
    // library reads GDK_BACKEND only once during its first gdk_display_open()
    // call.  Calling the libc setenv() directly modifies the process environ[]
    // array that GTK reads, which is the only reliable way to inject this
    // setting before dlopening libsciter.so.
    [DllImport("libc", EntryPoint = "setenv", SetLastError = true,
               CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern int LibcSetenv(string name, string value, int overwrite);

    private static void EnsureGdkX11()
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix) return;

        var sessionType    = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        var gdkBackend     = Environment.GetEnvironmentVariable("GDK_BACKEND") ?? "";

        if (sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(waylandDisplay))
        {
            if (!gdkBackend.Equals("x11", StringComparison.OrdinalIgnoreCase))
            {
                // Use libc setenv so GTK4 (used by Sciter 6.0.3.6+) sees the
                // change before its first gdk_display_open() call.
                LibcSetenv("GDK_BACKEND", "x11", 1);
                Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
            }
            Logger.Info("Boot", "Wayland detected — set GDK_BACKEND=x11 (XWayland) for Sciter GTK4 backend");
        }

        // ── Sciter 6.0.3.6+: GTK4 backend — dlopen("libgtk-4.so") fix ────────
        // Sciter dynamically loads GTK4 by looking for the UNVERSIONED name
        // "libgtk-4.so".  On Debian/Ubuntu the unversioned symlink is only
        // created by libgtk-4-dev.  Without it dlopen returns NULL, and Sciter's
        // Wayland/X11 fallback path has a NULL-pointer bug → SIGSEGV.
        //
        // Fix: create a local "libgtk-4.so" symlink in the application's own
        // binary directory and prepend that directory to LD_LIBRARY_PATH so
        // Sciter's runtime dlopen() call finds it.
        EnsureGtk4UnversionedSymlink();
    }

    private static void EnsureGtk4UnversionedSymlink()
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix) return;

        const string unversioned = "libgtk-4.so";
        const string versioned   = "libgtk-4.so.1";

        // If the unversioned name is already resolvable system-wide, nothing to do.
        static bool SystemHas(string name) =>
            File.Exists($"/lib/x86_64-linux-gnu/{name}") ||
            File.Exists($"/usr/lib/x86_64-linux-gnu/{name}") ||
            File.Exists($"/usr/local/lib/{name}") ||
            File.Exists($"/lib/{name}") ||
            File.Exists($"/usr/lib/{name}");

        if (SystemHas(unversioned)) return;

        // Find the versioned library on this machine.
        string? versionedPath = null;
        foreach (var dir in new[] {
            "/lib/x86_64-linux-gnu",
            "/usr/lib/x86_64-linux-gnu",
            "/lib/aarch64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/lib64", "/usr/lib", "/lib" })
        {
            var c = Path.Combine(dir, versioned);
            if (File.Exists(c)) { versionedPath = c; break; }
        }

        if (versionedPath == null) return; // GTK4 not installed at all — nothing to do

        // Create a symlink inside the app binary directory.
        var appBinDir   = AppDomain.CurrentDomain.BaseDirectory;
        var symlinkPath = Path.Combine(appBinDir, unversioned);

        try
        {
            if (!File.Exists(symlinkPath))
                File.CreateSymbolicLink(symlinkPath, versionedPath);

            // Prepend the binary dir to LD_LIBRARY_PATH so Sciter's dlopen
            // finds "libgtk-4.so" there.  dlopen() re-reads LD_LIBRARY_PATH on
            // every call, so this takes effect before Sciter's SCITER_APP_INIT.
            var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
            var newPath  = string.IsNullOrEmpty(existing)
                ? appBinDir
                : $"{appBinDir}:{existing}";
            LibcSetenv("LD_LIBRARY_PATH", newPath, 1);
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", newPath);

            Logger.Info("Boot", $"GTK4: created {symlinkPath} → {versionedPath} (install libgtk-4-dev to avoid this workaround)");
        }
        catch (Exception ex)
        {
            Logger.Warning("Boot", $"GTK4: could not create libgtk-4.so symlink: {ex.Message}");
        }
    }
    static async Task Main(string[] args)
    {
        // ── GTK/Wayland fix — must be FIRST, before any native lib loads ──────
        EnsureGdkX11();

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

        // ── Create Sciter host ────────────────────────────────────────────────
        // Console.SetOut redirect is intentionally deferred until AFTER
        // CreateMainWindow so that any GTK/Sciter crash messages are visible
        // in the terminal instead of being swallowed by the redirector.
        Logger.Info("Boot", "Creating SciterAPIHost...");
        var host = new SciterAPIHost(sciterLibDir);
        Logger.Info("Boot", "SciterAPIHost created, calling CreateMainWindow...");

        host.CreateMainWindow(
            1280, 800,
            enableDebug: IsDebugBuild(),
            enableFeature: true);

        Logger.Info("Boot", "Sciter window created");

        // ── Redirect Console.Out/Error now that the window is up ─────────────
        // Sciter's own diagnostic output goes through our structured Logger.
        var sciterLog = new SciterStdoutRedirector();
        Console.SetOut(sciterLog);
        Console.SetError(sciterLog);

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
