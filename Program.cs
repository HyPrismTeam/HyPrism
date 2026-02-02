using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Photino.NET;
using HyPrism.Backend;

namespace HyPrism;

internal sealed class FilteringTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly string[] _suppressTokens;

    public FilteringTextWriter(TextWriter inner, string[] suppressTokens)
    {
        _inner = inner;
        _suppressTokens = suppressTokens;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(string? value)
    {
        if (ShouldSuppress(value)) return;
        _inner.Write(value);
    }

    public override void WriteLine(string? value)
    {
        if (ShouldSuppress(value)) return;
        _inner.WriteLine(value);
    }

    private bool ShouldSuppress(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var token in _suppressTokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

class Program
{
    private static HttpListener? _server;
    private static int _port = 49152;
    private static string _wwwroot = "";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    [STAThread]
    static void Main(string[] args)
    {
        Logger.LogFile = Path.Combine(AppService.GetDefaultAppDir(), "logs", "hyprism.log");
        Logger.MinimumLevel = AppLogLevel.Info; // show Info-level logs in wrapper mode
        
        // Initialize backend services
        var app = new AppService();
        
        // Disable GPU acceleration on ALL platforms - use software rendering only
        // This ensures consistent behavior and avoids GPU-related issues
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Force software rendering to avoid GPU/GBM issues
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
            // Prefer Wayland over X11
            Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland,x11");
            // Disable GPU for GTK WebKit
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Disable GPU for WebView2 on Windows
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-gpu --disable-gpu-compositing");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Disable GPU acceleration for WebKit on macOS
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
        }
        
        // Get the wwwroot directory
        _wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(_wwwroot))
        {
            // macOS app bundle fallback: Contents/Resources/wwwroot
            var resourcesWwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "wwwroot"));
            if (Directory.Exists(resourcesWwwroot))
            {
                _wwwroot = resourcesWwwroot;
            }
        }
        
        // Start local HTTP server for serving static files
        StartLocalServer();
        
        // Create window
        var window = new PhotinoWindow()
            .SetTitle("HyPrism")
            .SetSize(1280, 800)
            .SetMinSize(1024, 700)
            .Center()
            .SetDevToolsEnabled(false)
            .SetUseOsDefaultSize(false)
            .SetGrantBrowserPermissions(true)
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                var win = (PhotinoWindow)sender!;
                HandleMessage(win, message, app);
            });
        
        // Set main window reference for game state events
        app.SetMainWindow(window);
        
        // Load from local HTTP server (bypasses file:// security restrictions)
        
        var isWrapper = args?.Any(a => a == "--wrapper") == true;
        if (isWrapper)
        {
            Logger.Info("Program", "Starting in wrapper mode");
        }

        // Inform backend that we are running in wrapper mode (affects auto-update behavior)
        app.SetWrapperMode(isWrapper);

        var url = isWrapper ? $"http://localhost:{_port}/index.html?wrapper=1" : $"http://localhost:{_port}/index.html";
        window.Load(url);
        
                // Suppress noisy Photino debug output on stdout/stderr while keeping our own logs
                // Photino writes lines like: Photino.NET: "HyPrism".Load(...)
                Console.SetOut(new FilteringTextWriter(Console.Out, new[] { "Photino.NET:" }));
                Console.SetError(new FilteringTextWriter(Console.Error, new[] { "Photino.NET:" }));
        
        Logger.Success("HyPrism", "Launcher started");
        
        // Check for updates after window loads (async, non-blocking)
        Task.Run(async () =>
        {
            await Task.Delay(2000); // Wait for UI to load
            await app.CheckForLauncherUpdatesAsync();
        });
        
        window.WaitForClose();
        
        // Stop server when window closes
        _server?.Stop();
    }
    
    static void StartLocalServer()
    {
        // Find an available port
        for (int i = 0; i < 100; i++)
        {
            try
            {
                _server = new HttpListener();
                _server.Prefixes.Add($"http://localhost:{_port}/");
                _server.Start();
                Logger.Info("Server", $"Started on port {_port}");
                
                // Handle requests in background
                Task.Run(() => HandleRequests());
                return;
            }
            catch
            {
                _port++;
            }
        }
        throw new Exception("Could not start local server");
    }
    
    static void HandleRequests()
    {
        while (_server?.IsListening == true)
        {
            try
            {
                var context = _server.GetContext();
                Task.Run(() => ProcessRequest(context));
            }
            catch
            {
                // Server stopped
                break;
            }
        }
    }
    
    static void ProcessRequest(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.LocalPath ?? "/";
            if (path == "/") path = "/index.html";
            
            var filePath = Path.Combine(_wwwroot, path.TrimStart('/'));
            
            if (File.Exists(filePath))
            {
                var extension = Path.GetExtension(filePath).ToLower();
                context.Response.ContentType = extension switch
                {
                    ".html" => "text/html",
                    ".css" => "text/css",
                    ".js" => "application/javascript",
                    ".json" => "application/json",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".svg" => "image/svg+xml",
                    ".ogg" => "audio/ogg",
                    ".mp3" => "audio/mpeg",
                    ".woff" => "font/woff",
                    ".woff2" => "font/woff2",
                    _ => "application/octet-stream"
                };
                
                context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                context.Response.StatusCode = 200;
                
                var bytes = File.ReadAllBytes(filePath);
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Server", $"Request error: {ex.Message}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }
    
    static void HandleMessage(PhotinoWindow window, string message, AppService app)
    {
        try
        {
            var request = JsonSerializer.Deserialize<RpcRequest>(message);
            if (request == null) return;
            
            Task.Run(async () =>
            {
                object? result = null;
                string? error = null;
                
                try
                {
                    result = request.Method switch
                    {
                        // User/Config
                        "QueryConfig" => app.QueryConfig(),
                        "GetNick" => app.GetNick(),
                        "SetNick" => app.SetNick(GetArg<string>(request.Args, 0)),
                        "GetLauncherVersion" => app.GetLauncherVersion(),
                        "GetUUID" => app.GetUUID(),
                        "SetUUID" => app.SetUUID(GetArg<string>(request.Args, 0)),
                        "GetAvatarPreview" => app.GetAvatarPreview(),
                        "GetAvatarPreviewForUUID" => app.GetAvatarPreviewForUUID(GetArg<string>(request.Args, 0)),
                        "ClearAvatarCache" => app.ClearAvatarCache(),
                        
                        // Profile Management
                        "GetProfiles" => app.GetProfiles(),
                        "GetActiveProfileIndex" => app.GetActiveProfileIndex(),
                        "CreateProfile" => app.CreateProfile(GetArg<string>(request.Args, 0), GetArg<string>(request.Args, 1)),
                        "DuplicateProfile" => app.DuplicateProfile(GetArg<string>(request.Args, 0)),
                        "DuplicateProfileWithoutData" => app.DuplicateProfileWithoutData(GetArg<string>(request.Args, 0)),
                        "DeleteProfile" => app.DeleteProfile(GetArg<string>(request.Args, 0)),
                        "SwitchProfile" => app.SwitchProfile(GetArg<int>(request.Args, 0)),
                        "UpdateProfile" => app.UpdateProfile(GetArg<string>(request.Args, 0), GetArg<string>(request.Args, 1), GetArg<string>(request.Args, 2)),
                        "SaveCurrentAsProfile" => app.SaveCurrentAsProfile(),
                        "OpenCurrentProfileFolder" => app.OpenCurrentProfileFolder(),
                        
                        "GetCustomInstanceDir" => app.GetCustomInstanceDir(),
                        "GetDefaultInstanceDir" => app.GetDefaultInstanceDir(),
                        "SetInstanceDirectory" => await app.SetInstanceDirectoryAsync(GetArg<string>(request.Args, 0)),
                        
                        // Version Management
                        "GetVersionType" => app.GetVersionType(),
                        "SetVersionType" => app.SetVersionType(GetArg<string>(request.Args, 0)),
                        "GetVersionList" => await app.GetVersionListAsync(GetArg<string>(request.Args, 0)),
                        "SetSelectedVersion" => app.SetSelectedVersion(GetArg<int>(request.Args, 0)),
                        "IsVersionInstalled" => app.IsVersionInstalled(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        "GetInstalledVersionsForBranch" => app.GetInstalledVersionsForBranch(GetArg<string>(request.Args, 0)),
                        "CheckLatestNeedsUpdate" => await app.CheckLatestNeedsUpdateAsync(GetArg<string>(request.Args, 0)),
                        "GetLatestVersionStatus" => await GetLatestVersionStatusWithLogging(app, GetArg<string>(request.Args, 0)),
                        "GetPendingUpdateInfo" => await app.GetPendingUpdateInfoAsync(GetArg<string>(request.Args, 0)),
                        "CopyUserData" => await app.CopyUserDataAsync(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1), GetArg<int>(request.Args, 2)),
                        "ForceUpdateLatest" => await app.ForceUpdateLatestAsync(GetArg<string>(request.Args, 0)),
                        "DuplicateLatest" => await app.DuplicateLatestAsync(GetArg<string>(request.Args, 0)),
                        
                        // Assets
                        "HasAssetsZip" => app.HasAssetsZip(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        "GetAssetsZipPath" => app.GetAssetsZipPath(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        
                        // Game
                        "DownloadAndLaunch" => await app.DownloadAndLaunchAsync(window),
                        "DownloadOnly" => await app.DownloadOnlyAsync(),
                        "LaunchOnly" => await app.LaunchOnlyAsync(),
                        "CancelDownload" => app.CancelDownload(),
                        "IsGameRunning" => app.IsGameRunning(),
                        "GetRecentLogs" => app.GetRecentLogs(GetArg<int>(request.Args, 0)),
                        "ExitGame" => app.ExitGame(),
                        "DeleteGame" => app.DeleteGame(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        
                        // Folder
                        "OpenFolder" => app.OpenFolder(),
                        "SelectInstanceDirectory" => await app.SelectInstanceDirectoryAsync(),
                        "BrowseFolder" => await app.BrowseFolderAsync(GetArg<string?>(request.Args, 0)),
                        "BrowseModFiles" => await app.BrowseModFilesAsync(),
                        
                        // News
                        "GetNews" => await app.GetNewsAsync(GetArg<int>(request.Args, 0)),

                        // Wrapper (installed-by-packaging containers like Flatpak/AppImage)
                        "WrapperGetStatus" => await app.GetWrapperStatusAsync(),
                        "WrapperInstallLatest" => await app.WrapperInstallLatestAsync(),
                        "WrapperLaunch" => app.WrapperLaunch(),

                        // Update
                        "Update" => await app.UpdateAsync(request.Args),
                        
                        // Browser
                        "BrowserOpenURL" => app.BrowserOpenURL(GetArg<string>(request.Args, 0)),
                        
                        // Music
                        "GetMusicEnabled" => app.GetMusicEnabled(),
                        "SetMusicEnabled" => app.SetMusicEnabled(GetArg<bool>(request.Args, 0)),
                        
                        // Online Mode removed: launcher always runs offline
                        
                        // Mod Manager
                        "SearchMods" => await app.SearchModsAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1),
                            GetArg<int>(request.Args, 2),
                            GetArg<string[]>(request.Args, 3),
                            GetArg<int>(request.Args, 4),
                            GetArg<int>(request.Args, 5)),
                        "GetModFiles" => await app.GetModFilesAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1),
                            GetArg<int>(request.Args, 2)),
                        "GetModCategories" => await app.GetModCategoriesAsync(),
                        "GetInstanceInstalledMods" => app.GetInstanceInstalledMods(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "InstallModFileToInstance" => await app.InstallModFileToInstanceAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<string>(request.Args, 2),
                            GetArg<int>(request.Args, 3)),
                        "InstallOptimizationMods" => await app.InstallOptimizationModsAsync(),
                        "UninstallInstanceMod" => app.UninstallInstanceMod(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<int>(request.Args, 2)),
                        "OpenInstanceModsFolder" => app.OpenInstanceModsFolder(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "OpenInstanceFolder" => app.OpenInstanceFolder(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "CheckInstanceModUpdates" => await app.CheckInstanceModUpdatesAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "InstallLocalModFile" => await app.InstallLocalModFile(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<int>(request.Args, 2)),
                        "InstallModFromBase64" => await app.InstallModFromBase64(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<string>(request.Args, 2),
                            GetArg<int>(request.Args, 3)),
                        "ExportModList" => app.ExportModList(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "ExportModsToFolder" => await app.ExportModsToFolder(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1),
                            GetArg<string>(request.Args, 2),
                            GetArg<string>(request.Args, 3)),
                        "GetLastExportPath" => app.GetLastExportPath(),
                        "ImportModList" => await app.ImportModList(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<int>(request.Args, 2)),
                        "GetInstalledVersionsDetailed" => app.GetInstalledVersionsDetailed(),
                        "ExportInstance" => app.ExportInstance(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        
                        // Settings
                        "GetLauncherBranch" => app.GetLauncherBranch(),
                        "SetLauncherBranch" => app.SetLauncherBranch(GetArg<string>(request.Args, 0)),
                        "CheckRosettaStatus" => app.CheckRosettaStatus(),
                        "GetCloseAfterLaunch" => app.GetCloseAfterLaunch(),
                        "SetCloseAfterLaunch" => app.SetCloseAfterLaunch(GetArg<bool>(request.Args, 0)),
                        "GetShowDiscordAnnouncements" => app.GetShowDiscordAnnouncements(),
                        "SetShowDiscordAnnouncements" => app.SetShowDiscordAnnouncements(GetArg<bool>(request.Args, 0)),
                        "DismissAnnouncement" => app.DismissAnnouncement(GetArg<string>(request.Args, 0)),
                        "OpenLauncherFolder" => app.OpenLauncherFolder(),
                        "DeleteLauncherData" => app.DeleteLauncherData(),
                        "GetLauncherFolderPath" => app.GetLauncherFolderPath(),
                        "GetTestAnnouncement" => app.GetTestAnnouncement(),
                        
                        // News settings
                        "GetDisableNews" => app.GetDisableNews(),
                        "SetDisableNews" => app.SetDisableNews(GetArg<bool>(request.Args, 0)),
                        
                        // Mod settings
                        "GetShowAlphaMods" => app.GetShowAlphaMods(),
                        "SetShowAlphaMods" => app.SetShowAlphaMods(GetArg<bool>(request.Args, 0)),
                        
                        // Background settings
                        "GetBackgroundMode" => app.GetBackgroundMode(),
                        "SetBackgroundMode" => app.SetBackgroundMode(GetArg<string>(request.Args, 0)),
                        "GetAvailableBackgrounds" => app.GetAvailableBackgrounds(),
                        
                        // Accent color settings
                        "GetAccentColor" => app.GetAccentColor(),
                        "SetAccentColor" => app.SetAccentColor(GetArg<string>(request.Args, 0)),
                        
                        // Launcher Data Directory
                        "GetLauncherDataDirectory" => app.GetLauncherDataDirectory(),
                        "SetLauncherDataDirectory" => await app.SetLauncherDataDirectoryAsync(GetArg<string>(request.Args, 0)),
                        
                        // Onboarding
                        "GetHasCompletedOnboarding" => app.GetHasCompletedOnboarding(),
                        "SetHasCompletedOnboarding" => app.SetHasCompletedOnboarding(GetArg<bool>(request.Args, 0)),
                        "GetRandomUsername" => app.GetRandomUsername(),
                        "ResetOnboarding" => app.ResetOnboarding(),
                        
                        // Online mode
                        "GetOnlineMode" => app.GetOnlineMode(),
                        "SetOnlineMode" => app.SetOnlineMode(GetArg<bool>(request.Args, 0)),
                        
                        // Auth domain
                        "GetAuthDomain" => app.GetAuthDomain(),
                        "SetAuthDomain" => app.SetAuthDomain(GetArg<string>(request.Args, 0)),
                        
                        // Game Language
                        "SetGameLanguage" => await app.SetGameLanguageAsync(GetArg<string>(request.Args, 0)),
                        "GetAvailableGameLanguages" => app.GetAvailableGameLanguages(),
                        
                        // Discord
                        "GetDiscordAnnouncement" => await app.GetDiscordAnnouncementAsync(),
                        "ReactToAnnouncement" => await app.ReactToAnnouncementAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1)),
                        
                        // Window Controls
                        "WindowMinimize" => SetMinimized(window, true),
                        "WindowMaximize" => ToggleMaximize(window),
                        "WindowClose" => ExitApp(),
                        
                        _ => throw new Exception($"Unknown method: {request.Method}")
                    };
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Logger.Error("RPC", $"{request.Method}: {ex.Message}");
                }
                
                var response = new RpcResponse
                {
                    Id = request.Id,
                    Result = result,
                    Error = error
                };
                
                var json = JsonSerializer.Serialize(response, JsonOptions);
                window.SendWebMessage(json);
            });
        }
        catch (Exception ex)
        {
            Logger.Error("RPC", $"Error handling message: {ex.Message}");
        }
    }
    
    static bool SetMinimized(PhotinoWindow window, bool value)
    {
        window.Minimized = value;
        return true;
    }
    
    static bool ToggleMaximize(PhotinoWindow window)
    {
        window.Maximized = !window.Maximized;
        return true;
    }
    
    static bool ExitApp()
    {
        Environment.Exit(0);
        return true;
    }
    
    static T GetArg<T>(JsonElement[]? args, int index)
    {
        if (args == null || args.Length <= index)
            return default!;
        
        return JsonSerializer.Deserialize<T>(args[index].GetRawText())!;
    }
    
    static async Task<VersionStatus> GetLatestVersionStatusWithLogging(AppService app, string branch)
    {
        var status = await app.GetLatestVersionStatusAsync(branch);
        Logger.Info("RPC", $"GetLatestVersionStatus returned: Status={status.Status}, Installed={status.InstalledVersion}, Latest={status.LatestVersion}");
        return status;
    }
}

public class RpcRequest
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public JsonElement[]? Args { get; set; }
}

public class RpcResponse
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("Result")]
    public object? Result { get; set; }
    
    [JsonPropertyName("Error")]
    public string? Error { get; set; }
}
