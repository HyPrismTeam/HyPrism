using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Photino.NET;
using HyPrism.Backend;

namespace HyPrism;

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
        // Initialize backend services
        var app = new AppService();
        
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
            .SetDevToolsEnabled(true)
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
        window.Load($"http://localhost:{_port}/index.html");
        
        Logger.Success("HyPrism", "Launcher started");
        
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
                        "GetCustomInstanceDir" => app.GetCustomInstanceDir(),
                        "SetInstanceDirectory" => await app.SetInstanceDirectoryAsync(GetArg<string>(request.Args, 0)),
                        
                        // Version Management
                        "GetVersionType" => app.GetVersionType(),
                        "SetVersionType" => app.SetVersionType(GetArg<string>(request.Args, 0)),
                        "GetVersionList" => await app.GetVersionListAsync(GetArg<string>(request.Args, 0)),
                        "SetSelectedVersion" => app.SetSelectedVersion(GetArg<int>(request.Args, 0)),
                        "IsVersionInstalled" => app.IsVersionInstalled(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        "GetInstalledVersionsForBranch" => app.GetInstalledVersionsForBranch(GetArg<string>(request.Args, 0)),
                        "CheckLatestNeedsUpdate" => await app.CheckLatestNeedsUpdateAsync(GetArg<string>(request.Args, 0)),
                        
                        // Game
                        "DownloadAndLaunch" => await app.DownloadAndLaunchAsync(window),
                        "CancelDownload" => app.CancelDownload(),
                        "IsGameRunning" => app.IsGameRunning(),
                        "GetRecentLogs" => app.GetRecentLogs(GetArg<int>(request.Args, 0)),
                        "ExitGame" => app.ExitGame(),
                        "DeleteGame" => app.DeleteGame(GetArg<string>(request.Args, 0), GetArg<int>(request.Args, 1)),
                        
                        // Folder
                        "OpenFolder" => app.OpenFolder(),
                        "SelectInstanceDirectory" => await app.SelectInstanceDirectoryAsync(),
                        
                        // News
                        "GetNews" => await app.GetNewsAsync(GetArg<int>(request.Args, 0)),
                        
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
                        "UninstallInstanceMod" => app.UninstallInstanceMod(
                            GetArg<string>(request.Args, 0),
                            GetArg<string>(request.Args, 1),
                            GetArg<int>(request.Args, 2)),
                        "OpenInstanceModsFolder" => app.OpenInstanceModsFolder(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        "CheckInstanceModUpdates" => await app.CheckInstanceModUpdatesAsync(
                            GetArg<string>(request.Args, 0),
                            GetArg<int>(request.Args, 1)),
                        
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
