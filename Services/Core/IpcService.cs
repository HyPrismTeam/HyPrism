using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection;
using HyPrism.Models;
using HyPrism.Services.Game;
using HyPrism.Services.User;

namespace HyPrism.Services.Core;

/// <summary>
/// Central IPC service — bridges Electron IPC channels to .NET services.
/// Registered as a singleton via DI in Bootstrapper.cs.
/// Each channel follows the pattern: "hyprism:{domain}:{action}"
///
/// Structured @ipc annotations are parsed by Scripts/generate-ipc.mjs
/// to auto-generate Frontend/src/lib/ipc.ts (the ONLY IPC file).
///
/// These @type blocks define TypeScript interfaces emitted into the
/// generated ipc.ts. The C# code never reads them — they are only
/// consumed by the codegen script.
/// </summary>
/// 
/// @type ProgressUpdate { state: string; progress: number; messageKey: string; args?: unknown[]; downloadedBytes: number; totalBytes: number; }
/// @type GameState { state: 'starting' | 'started' | 'running' | 'stopped'; exitCode: number; }
/// @type GameError { type: string; message: string; technical?: string; }
/// @type NewsItem { title: string; excerpt?: string; url?: string; date?: string; publishedAt?: string; author?: string; imageUrl?: string; source?: string; }
/// @type Profile { id: string; name: string; uuid?: string; isOfficial?: boolean; avatar?: string; folderName?: string; }
/// @type HytaleAuthStatus { loggedIn: boolean; username?: string; uuid?: string; error?: string; errorType?: string; }
/// @type ProfileSnapshot { nick: string; uuid: string; avatarPath?: string; }
/// @type SettingsSnapshot { language: string; musicEnabled: boolean; launcherBranch: string; closeAfterLaunch: boolean; showDiscordAnnouncements: boolean; disableNews: boolean; backgroundMode: string; availableBackgrounds: string[]; accentColor: string; hasCompletedOnboarding: boolean; onlineMode: boolean; authDomain: string; dataDirectory: string; gpuPreference?: string; launchOnStartup?: boolean; minimizeToTray?: boolean; animations?: boolean; transparency?: boolean; resolution?: string; ramMb?: number; sound?: boolean; closeOnLaunch?: boolean; developerMode?: boolean; verboseLogging?: boolean; preRelease?: boolean; [key: string]: unknown; }
/// @type ModScreenshot { id: number; title: string; thumbnailUrl: string; url: string; }
/// @type ModInfo { id: string; name: string; slug: string; summary: string; author: string; downloadCount: number; iconUrl: string; thumbnailUrl: string; categories: string[]; dateUpdated: string; latestFileId: string; screenshots: ModScreenshot[]; }
/// @type ModSearchResult { mods: ModInfo[]; totalCount: number; }
/// @type ModFileInfo { id: string; modId: string; fileName: string; displayName: string; downloadUrl: string; fileLength: number; fileDate: string; releaseType: number; gameVersions: string[]; downloadCount: number; }
/// @type ModFilesResult { files: ModFileInfo[]; totalCount: number; }
/// @type ModCategory { id: number; name: string; slug: string; }
/// @type InstalledMod { id: string; name: string; slug?: string; version?: string; fileId?: string; fileName?: string; enabled: boolean; author?: string; description?: string; iconUrl?: string; curseForgeId?: string; fileDate?: string; releaseType?: number; latestFileId?: string; latestVersion?: string; screenshots?: ModScreenshot[]; }
/// @type SaveInfo { name: string; previewPath?: string; lastModified?: string; sizeBytes?: number; }
/// @type AppConfig { language: string; dataDirectory: string; [key: string]: unknown; }
/// @type InstalledInstance { branch: string; version: number; path: string; hasUserData: boolean; userDataSize: number; totalSize: number; isValid: boolean; }
/// @type LanguageInfo { code: string; name: string; }
/// @type GpuAdapterInfo { name: string; vendor: string; type: string; }
public class IpcService
{
    private readonly IServiceProvider _services;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public IpcService(IServiceProvider services)
    {
        _services = services;
    }

    private static BrowserWindow? GetMainWindow()
    {
        return Electron.WindowManager.BrowserWindows.FirstOrDefault();
    }

    /// <summary>
    /// Converts IPC args to a JSON string for deserialization.
    /// The renderer sends JSON.stringify(data), so args is typically a string.
    /// But ElectronNET may also deliver a JsonElement or other deserialized object.
    /// </summary>
    private static string ArgsToJson(object? args)
    {
        if (args is null) return "{}";
        if (args is string s) return s;
        if (args is JsonElement je) return je.GetRawText();
        // Fallback: re-serialize whatever C# object ElectronNET produced
        return JsonSerializer.Serialize(args, JsonOpts);
    }

    /// <summary>
    /// Extracts a plain string from IPC args (for channels that expect a single string value).
    /// The renderer sends JSON.stringify("someValue") which produces '"someValue"',
    /// so we need to unwrap the outer quotes.
    /// </summary>
    private static string ArgsToString(object? args)
    {
        if (args is null) return string.Empty;
        var raw = args.ToString() ?? string.Empty;
        // If the renderer sent JSON.stringify("text"), we get a JSON-quoted string
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(raw) ?? raw; }
            catch { /* fall through */ }
        }
        return raw;
    }

    private static void Reply(string channel, object data)
    {
        var win = GetMainWindow();
        if (win == null) return;
        Electron.IpcMain.Send(win, channel, JsonSerializer.Serialize(data, JsonOpts));
    }

    private static void ReplyRaw(string channel, string raw)
    {
        var win = GetMainWindow();
        if (win == null) return;
        Electron.IpcMain.Send(win, channel, raw);
    }

    public void RegisterAll()
    {
        Logger.Info("IPC", "Registering IPC handlers...");

        RegisterConfigHandlers();
        RegisterGameHandlers();
        RegisterInstanceHandlers();
        RegisterNewsHandlers();
        RegisterProfileHandlers();
        RegisterAuthHandlers();
        RegisterSettingsHandlers();
        RegisterLocalizationHandlers();
        RegisterWindowHandlers();
        RegisterModHandlers();
        RegisterSystemHandlers();
        RegisterConsoleHandlers();
        RegisterFileDialogHandlers();

        Logger.Success("IPC", "All IPC handlers registered");
    }

    // #region Config
    // @ipc invoke hyprism:config:get -> AppConfig
    // @ipc invoke hyprism:config:save -> { success: boolean }

    private void RegisterConfigHandlers()
    {
        var config = _services.GetRequiredService<IConfigService>();

        Electron.IpcMain.On("hyprism:config:get", (_) =>
        {
            Reply("hyprism:config:get:reply", config.Configuration);
        });

        Electron.IpcMain.On("hyprism:config:save", (_) =>
        {
            try
            {
                config.SaveConfig();
                Reply("hyprism:config:save:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Config save failed: {ex.Message}");
                Reply("hyprism:config:save:reply", new { success = false, error = ex.Message });
            }
        });
    }

    // #endregion

    // #region Game Session
    // @ipc send hyprism:game:launch
    // @ipc send hyprism:game:cancel
    // @ipc invoke hyprism:game:instances -> InstalledInstance[]
    // @ipc invoke hyprism:game:isRunning -> boolean
    // @ipc invoke hyprism:game:versions -> number[]
    // @ipc event hyprism:game:progress -> ProgressUpdate
    // @ipc event hyprism:game:state -> GameState
    // @ipc event hyprism:game:error -> GameError

    private void RegisterGameHandlers()
    {
        var gameSession = _services.GetRequiredService<IGameSessionService>();
        var progressService = _services.GetRequiredService<ProgressNotificationService>();
        var instanceService = _services.GetRequiredService<IInstanceService>();
        var gameProcessService = _services.GetRequiredService<IGameProcessService>();
        var versionService = _services.GetRequiredService<IVersionService>();
        var configService = _services.GetRequiredService<IConfigService>();

        // Push events from .NET → React
        progressService.DownloadProgressChanged += (msg) =>
        {
            try { Reply("hyprism:game:progress", msg); } catch { /* swallow */ }
        };

        progressService.GameStateChanged += (state, exitCode) =>
        {
            Logger.Info("IPC", $"Sending game-state event: state={state}, exitCode={exitCode}");
            try { Reply("hyprism:game:state", new { state, exitCode }); } catch { /* swallow */ }
        };

        progressService.ErrorOccurred += (type, message, technical) =>
        {
            try { Reply("hyprism:game:error", new { type, message, technical }); } catch { /* swallow */ }
        };

        Electron.IpcMain.On("hyprism:game:launch", async (args) =>
        {
            // First check if game is already running
            if (gameProcessService.IsGameRunning())
            {
                Logger.Warning("IPC", "Game launch request ignored - game already running");
                return;
            }
            
            // Optionally accept branch and version to launch a specific instance
            if (args != null)
            {
                try
                {
                    var json = ArgsToJson(args);
                    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                    if (data != null)
                    {
                        if (data.TryGetValue("branch", out var branchEl))
                        {
                            var branchValue = branchEl.GetString() ?? "release";
                            configService.Configuration.VersionType = branchValue;
                            configService.Configuration.LauncherBranch = branchValue;
                        }
                        if (data.TryGetValue("version", out var versionEl))
                        {
                            configService.Configuration.SelectedVersion = versionEl.GetInt32();
                        }
                    }
                }
                catch { /* ignore parsing errors, use current config */ }
            }
            
            Logger.Info("IPC", "Game launch requested");
            try { await gameSession.DownloadAndLaunchAsync(); }
            catch (Exception ex) { Logger.Error("IPC", $"Game launch failed: {ex.Message}"); }
        });

        Electron.IpcMain.On("hyprism:game:cancel", (_) =>
        {
            Logger.Info("IPC", "Game download cancel requested");
            gameSession.CancelDownload();
        });

        Electron.IpcMain.On("hyprism:game:instances", (_) =>
        {
            try
            {
                var instances = instanceService.GetInstalledInstances();
                Logger.Debug("IPC", $"Returning {instances.Count} installed instances");
                Reply("hyprism:game:instances:reply", instances);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get instances: {ex.Message}");
                Reply("hyprism:game:instances:reply", new List<object>());
            }
        });

        Electron.IpcMain.On("hyprism:game:isRunning", (_) =>
        {
            try
            {
                var isRunning = gameProcessService.CheckForRunningGame();
                Reply("hyprism:game:isRunning:reply", isRunning);
            }
            catch (Exception ex)
            {
                Reply("hyprism:game:isRunning:reply", false);
            }
        });

        Electron.IpcMain.On("hyprism:game:versions", async (args) =>
        {
            try
            {
                string branch = configService.Configuration.VersionType ?? "release";
                if (args != null)
                {
                    var json = ArgsToJson(args);
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
                    if (data != null && data.TryGetValue("branch", out var b) && !string.IsNullOrEmpty(b))
                    {
                        branch = b;
                    }
                }
                
                var versions = await versionService.GetVersionListAsync(branch);
                Logger.Info("IPC", $"Returning {versions.Count} available versions for branch {branch}");
                Reply("hyprism:game:versions:reply", versions);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get versions: {ex.Message}");
                Reply("hyprism:game:versions:reply", new List<int>());
            }
        });
    }
    // #endregion

    // #region Instance Management
    // @ipc invoke hyprism:instance:create -> boolean
    // @ipc invoke hyprism:instance:delete -> boolean
    // @ipc send hyprism:instance:openFolder
    // @ipc send hyprism:instance:openModsFolder
    // @ipc invoke hyprism:instance:export -> string
    // @ipc invoke hyprism:instance:import -> boolean
    // @ipc invoke hyprism:instance:saves -> SaveInfo[]
    // @ipc send hyprism:instance:openSaveFolder
    // @ipc invoke hyprism:instance:getIcon -> string | null

    private void RegisterInstanceHandlers()
    {
        var instanceService = _services.GetRequiredService<IInstanceService>();
        var fileService = _services.GetRequiredService<IFileService>();

        // Create an instance (directory + metadata only, no download)
        Electron.IpcMain.On("hyprism:instance:create", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var customName = data?.ContainsKey("customName") == true ? data["customName"].GetString() : null;

                // Create the instance directory
                var instancePath = instanceService.GetInstancePath(branch, version);
                Directory.CreateDirectory(instancePath);
                
                // Set custom name if provided
                if (!string.IsNullOrWhiteSpace(customName))
                {
                    instanceService.SetInstanceCustomName(branch, version, customName);
                }

                Logger.Success("IPC", $"Created instance {branch}/{version} (no download)");
                Reply("hyprism:instance:create:reply", true);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to create instance: {ex.Message}");
                Reply("hyprism:instance:create:reply", false);
            }
        });

        // Delete an instance
        Electron.IpcMain.On("hyprism:instance:delete", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var result = instanceService.DeleteGame(branch, version);
                Logger.Info("IPC", $"Deleted instance {branch}/{version}: {result}");
                Reply("hyprism:instance:delete:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to delete instance: {ex.Message}");
                Reply("hyprism:instance:delete:reply", false);
            }
        });

        // Open instance folder
        Electron.IpcMain.On("hyprism:instance:openFolder", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var path = instanceService.GetInstancePath(branch, version);
                if (Directory.Exists(path))
                {
                    fileService.OpenFolder(path);
                    Logger.Info("IPC", $"Opened folder: {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to open instance folder: {ex.Message}");
            }
        });

        // Open mods folder
        Electron.IpcMain.On("hyprism:instance:openModsFolder", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                var modsPath = Path.Combine(instancePath, "Client", "mods");
                
                if (!Directory.Exists(modsPath))
                {
                    Directory.CreateDirectory(modsPath);
                }
                
                fileService.OpenFolder(modsPath);
                Logger.Info("IPC", $"Opened mods folder: {modsPath}");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to open mods folder: {ex.Message}");
            }
        });

        // Export instance as zip
        Electron.IpcMain.On("hyprism:instance:export", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                if (!Directory.Exists(instancePath))
                {
                    Reply("hyprism:instance:export:reply", "");
                    return;
                }

                // Export to desktop by default
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var filename = $"HyPrism-{branch}-v{version}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                var savePath = Path.Combine(desktop, filename);

                // Create zip
                if (File.Exists(savePath)) File.Delete(savePath);
                ZipFile.CreateFromDirectory(instancePath, savePath, CompressionLevel.Optimal, false);
                
                Logger.Success("IPC", $"Exported instance to: {savePath}");
                Reply("hyprism:instance:export:reply", savePath);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to export instance: {ex.Message}");
                Reply("hyprism:instance:export:reply", "");
            }
        });

        // Import instance from zip (using file dialog service)
        Electron.IpcMain.On("hyprism:instance:import", async (_) =>
        {
            try
            {
                // For now, return false - import should be triggered from frontend with file picker
                Logger.Info("IPC", "Import triggered - frontend should use file picker");
                Reply("hyprism:instance:import:reply", false);
                return;
                
                /* Commented out until we have proper file dialog
                var zipPath = "";
                // Extract to a temp location first to check structure
                var tempDir = Path.Combine(Path.GetTempPath(), $"hyprism-import-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);
                
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                
                // Determine target path - check if zip has instance.json or similar metadata
                var metaPath = Path.Combine(tempDir, "instance.json");
                var branch = "release";
                var version = 0; // Latest
                
                if (File.Exists(metaPath))
                {
                    var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(metaPath), JsonOpts);
                    branch = meta?["branch"].GetString() ?? "release";
                    if (meta?.TryGetValue("version", out var v) == true) version = v.GetInt32();
                }
                
                var targetPath = instanceService.GetInstancePath(branch, version);
                
                // Move from temp to target
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
                Directory.Move(tempDir, targetPath);
                
                Logger.Success("IPC", $"Imported instance to: {targetPath}");
                Reply("hyprism:instance:import:reply", true);
                */
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to import instance: {ex.Message}");
                Reply("hyprism:instance:import:reply", false);
            }
        });

        // Get saves for an instance
        Electron.IpcMain.On("hyprism:instance:saves", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                var savesPath = Path.Combine(instancePath, "UserData", "Saves");
                
                var saves = new List<object>();
                
                if (Directory.Exists(savesPath))
                {
                    foreach (var saveDir in Directory.GetDirectories(savesPath))
                    {
                        var dirInfo = new DirectoryInfo(saveDir);
                        var previewPath = Path.Combine(saveDir, "preview.png");
                        
                        // Calculate total size
                        long sizeBytes = 0;
                        try
                        {
                            sizeBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                        }
                        catch { /* ignore */ }

                        saves.Add(new
                        {
                            name = dirInfo.Name,
                            path = saveDir,
                            previewPath = File.Exists(previewPath) ? $"file://{previewPath.Replace("\\", "/")}" : null,
                            lastModified = dirInfo.LastWriteTime.ToString("o"),
                            sizeBytes
                        });
                    }
                }
                
                Logger.Info("IPC", $"Found {saves.Count} saves for {branch}/{version}");
                Reply("hyprism:instance:saves:reply", saves);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get saves: {ex.Message}");
                Reply("hyprism:instance:saves:reply", new List<object>());
            }
        });

        // Open save folder
        Electron.IpcMain.On("hyprism:instance:openSaveFolder", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var saveName = data?["saveName"].GetString() ?? "";
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                var savePath = Path.Combine(instancePath, "UserData", "Saves", saveName);
                
                if (Directory.Exists(savePath))
                {
                    fileService.OpenFolder(savePath);
                    Logger.Info("IPC", $"Opened save folder: {savePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to open save folder: {ex.Message}");
            }
        });

        // Get instance icon
        Electron.IpcMain.On("hyprism:instance:getIcon", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                var iconPath = Path.Combine(instancePath, "icon.png");
                
                if (File.Exists(iconPath))
                {
                    Reply("hyprism:instance:getIcon:reply", $"file://{iconPath.Replace("\\", "/")}");
                }
                else
                {
                    Reply("hyprism:instance:getIcon:reply", null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get instance icon: {ex.Message}");
                Reply("hyprism:instance:getIcon:reply", null);
            }
        });

        // Set instance icon
        Electron.IpcMain.On("hyprism:instance:setIcon", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var iconPath = data?["iconPath"].GetString();
                
                var instancePath = instanceService.GetInstancePath(branch, version);
                var targetIconPath = Path.Combine(instancePath, "icon.png");
                
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    File.Copy(iconPath, targetIconPath, true);
                    Reply("hyprism:instance:setIcon:reply", true);
                    Logger.Info("IPC", $"Set icon for {branch}/{version}");
                }
                else
                {
                    // Icon path not provided or invalid
                    Reply("hyprism:instance:setIcon:reply", false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to set instance icon: {ex.Message}");
                Reply("hyprism:instance:setIcon:reply", false);
            }
        });

        // Rename instance (set custom name)
        Electron.IpcMain.On("hyprism:instance:rename", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var customName = data?["customName"].GetString();
                
                instanceService.SetInstanceCustomName(branch, version, customName);
                Reply("hyprism:instance:rename:reply", true);
                Logger.Info("IPC", $"Renamed instance {branch}/{version} to: {customName ?? "(default)"}");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to rename instance: {ex.Message}");
                Reply("hyprism:instance:rename:reply", false);
            }
        });
    }
    // #endregion

    // #region News
    // @ipc invoke hyprism:news:get -> NewsItem[]

    private void RegisterNewsHandlers()
    {
        var newsService = _services.GetRequiredService<INewsService>();

        Electron.IpcMain.On("hyprism:news:get", async (_) =>
        {
            try
            {
                var news = await newsService.GetNewsAsync();
                Reply("hyprism:news:get:reply", news);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"News fetch failed: {ex.Message}");
                Reply("hyprism:news:get:reply", new { error = ex.Message });
            }
        });
    }

    // #endregion

    // #region Profiles
    // @ipc invoke hyprism:profile:get -> ProfileSnapshot
    // @ipc invoke hyprism:profile:list -> Profile[]
    // @ipc invoke hyprism:profile:switch -> { success: boolean }
    // @ipc invoke hyprism:profile:setNick -> { success: boolean }
    // @ipc invoke hyprism:profile:setUuid -> { success: boolean }
    // @ipc invoke hyprism:profile:create -> Profile
    // @ipc invoke hyprism:profile:delete -> { success: boolean }
    // @ipc invoke hyprism:profile:activeIndex -> number
    // @ipc invoke hyprism:profile:save -> { success: boolean }
    // @ipc invoke hyprism:profile:duplicate -> Profile
    // @ipc send hyprism:profile:openFolder
    // @ipc invoke hyprism:profile:avatarForUuid -> string

    private void RegisterProfileHandlers()
    {
        var profileService = _services.GetRequiredService<IProfileService>();
        var profileMgmt = _services.GetRequiredService<IProfileManagementService>();

        Electron.IpcMain.On("hyprism:profile:get", (_) =>
        {
            Reply("hyprism:profile:get:reply", new
            {
                nick = profileService.GetNick(),
                uuid = profileService.GetUUID(),
                avatarPath = profileService.GetAvatarPreview()
            });
        });

        Electron.IpcMain.On("hyprism:profile:list", (_) =>
        {
            Reply("hyprism:profile:list:reply", profileMgmt.GetProfiles());
        });

        Electron.IpcMain.On("hyprism:profile:switch", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var index = doc.RootElement.GetProperty("index").GetInt32();
                Reply("hyprism:profile:switch:reply", new { success = profileMgmt.SwitchProfile(index) });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Profile switch failed: {ex.Message}");
                Reply("hyprism:profile:switch:reply", new { success = false });
            }
        });

        Electron.IpcMain.On("hyprism:profile:setNick", (args) =>
        {
            var nick = ArgsToString(args);
            var success = profileService.SetNick(nick);
            Reply("hyprism:profile:setNick:reply", new { success });
        });

        Electron.IpcMain.On("hyprism:profile:setUuid", (args) =>
        {
            var uuid = ArgsToString(args);
            var success = profileService.SetUUID(uuid);
            Reply("hyprism:profile:setUuid:reply", new { success });
        });

        Electron.IpcMain.On("hyprism:profile:create", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                var uuid = doc.RootElement.GetProperty("uuid").GetString() ?? "";
                var isOfficial = doc.RootElement.TryGetProperty("isOfficial", out var officialProp) && officialProp.GetBoolean();
                
                var profile = profileMgmt.CreateProfile(name, uuid);
                if (profile != null)
                {
                    profile.IsOfficial = isOfficial;
                    _services.GetRequiredService<ConfigService>().SaveConfig();
                }
                Reply("hyprism:profile:create:reply", profile != null ? (object)profile : new { error = "Failed to create profile" });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Profile create failed: {ex.Message}");
                Reply("hyprism:profile:create:reply", new { error = ex.Message });
            }
        });

        Electron.IpcMain.On("hyprism:profile:delete", (args) =>
        {
            var id = ArgsToString(args);
            var success = profileMgmt.DeleteProfile(id);
            Reply("hyprism:profile:delete:reply", new { success });
        });

        Electron.IpcMain.On("hyprism:profile:activeIndex", (_) =>
        {
            Reply("hyprism:profile:activeIndex:reply", profileMgmt.GetActiveProfileIndex());
        });

        Electron.IpcMain.On("hyprism:profile:save", (_) =>
        {
            var profile = profileMgmt.SaveCurrentAsProfile();
            Reply("hyprism:profile:save:reply", new { success = profile != null });
        });

        Electron.IpcMain.On("hyprism:profile:duplicate", (args) =>
        {
            var id = ArgsToString(args);
            var profile = profileMgmt.DuplicateProfileWithoutData(id);
            Reply("hyprism:profile:duplicate:reply", profile != null ? (object)profile : new { error = "Failed to duplicate" });
        });

        Electron.IpcMain.On("hyprism:profile:openFolder", (_) =>
        {
            profileMgmt.OpenCurrentProfileFolder();
        });

        Electron.IpcMain.On("hyprism:profile:avatarForUuid", (args) =>
        {
            var uuid = ArgsToString(args);
            var path = profileService.GetAvatarPreviewForUUID(uuid);
            Reply("hyprism:profile:avatarForUuid:reply", path ?? "");
        });
    }

    // #endregion

    // #region Hytale Auth
    // @ipc invoke hyprism:auth:status -> HytaleAuthStatus
    // @ipc invoke hyprism:auth:login -> HytaleAuthStatus
    // @ipc invoke hyprism:auth:logout -> { success: boolean }

    private void RegisterAuthHandlers()
    {
        var authService = _services.GetRequiredService<HytaleAuthService>();

        Electron.IpcMain.On("hyprism:auth:status", (_) =>
        {
            Reply("hyprism:auth:status:reply", authService.GetAuthStatus());
        });

        Electron.IpcMain.On("hyprism:auth:login", async (_) =>
        {
            try
            {
                var session = await authService.LoginAsync();
                Reply("hyprism:auth:login:reply", authService.GetAuthStatus());
            }
            catch (HytaleNoProfileException)
            {
                Logger.Warning("IPC", "Auth login: no Hytale game profile found");
                Reply("hyprism:auth:login:reply", new { loggedIn = false, errorType = "no_profile", error = "No game profiles found in this Hytale account" });
            }
            catch (HytaleAuthException ex)
            {
                Logger.Error("IPC", $"Auth login error ({ex.ErrorType}): {ex.Message}");
                Reply("hyprism:auth:login:reply", new { loggedIn = false, errorType = ex.ErrorType, error = ex.Message });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Auth login failed: {ex.Message}");
                Reply("hyprism:auth:login:reply", new { loggedIn = false, errorType = "unknown", error = ex.Message });
            }
        });

        Electron.IpcMain.On("hyprism:auth:logout", (_) =>
        {
            authService.Logout();
            Reply("hyprism:auth:logout:reply", new { success = true });
        });
    }

    // #endregion

    // #region Settings
    // @ipc invoke hyprism:settings:get -> SettingsSnapshot
    // @ipc invoke hyprism:settings:update -> { success: boolean }

    private void RegisterSettingsHandlers()
    {
        var settings = _services.GetRequiredService<ISettingsService>();

        Electron.IpcMain.On("hyprism:settings:get", (_) =>
        {
            var lang = settings.GetLanguage();
            Reply("hyprism:settings:get:reply", new
            {
                language = lang,
                musicEnabled = settings.GetMusicEnabled(),
                launcherBranch = settings.GetLauncherBranch(),
                versionType = settings.GetVersionType(),
                selectedVersion = settings.GetSelectedVersion(),
                closeAfterLaunch = settings.GetCloseAfterLaunch(),
                showDiscordAnnouncements = settings.GetShowDiscordAnnouncements(),
                disableNews = settings.GetDisableNews(),
                backgroundMode = settings.GetBackgroundMode(),
                availableBackgrounds = settings.GetAvailableBackgrounds(),
                accentColor = settings.GetAccentColor(),
                hasCompletedOnboarding = settings.GetHasCompletedOnboarding(),
                onlineMode = settings.GetOnlineMode(),
                authDomain = settings.GetAuthDomain(),
                dataDirectory = settings.GetLauncherDataDirectory(),
                gpuPreference = settings.GetGpuPreference(),
                launcherVersion = UpdateService.GetCurrentVersion()
            });
        });

        Electron.IpcMain.On("hyprism:settings:update", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var updates = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (updates != null)
                    foreach (var (key, value) in updates)
                        ApplySetting(settings, key, value);

                Reply("hyprism:settings:update:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Settings update failed: {ex.Message}");
                Reply("hyprism:settings:update:reply", new { success = false, error = ex.Message });
            }
        });
    }

    private static void ApplySetting(ISettingsService s, string key, JsonElement val)
    {
        switch (key)
        {
            case "language": s.SetLanguage(val.GetString() ?? "en-US"); break;
            case "musicEnabled": s.SetMusicEnabled(val.GetBoolean()); break;
            case "launcherBranch": s.SetLauncherBranch(val.GetString() ?? "release"); break;
            case "versionType": s.SetVersionType(val.GetString() ?? "release"); break;
            case "selectedVersion": s.SetSelectedVersion(val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0); break;
            case "closeAfterLaunch": s.SetCloseAfterLaunch(val.GetBoolean()); break;
            case "showDiscordAnnouncements": s.SetShowDiscordAnnouncements(val.GetBoolean()); break;
            case "disableNews": s.SetDisableNews(val.GetBoolean()); break;
            case "backgroundMode": s.SetBackgroundMode(val.GetString() ?? "default"); break;
            case "accentColor": s.SetAccentColor(val.GetString() ?? "#7C5CFC"); break;
            case "onlineMode": s.SetOnlineMode(val.GetBoolean()); break;
            case "authDomain": s.SetAuthDomain(val.GetString() ?? ""); break;
            case "gpuPreference": s.SetGpuPreference(val.GetString() ?? "dedicated"); break;
            default: Logger.Warning("IPC", $"Unknown setting key: {key}"); break;
        }
    }
    
    // #endregion

    // #region Localization
    // @ipc invoke hyprism:i18n:get -> Record<string, string>
    // @ipc invoke hyprism:i18n:current -> string
    // @ipc invoke hyprism:i18n:set -> { success: boolean, language: string }
    // @ipc invoke hyprism:i18n:languages -> LanguageInfo[]

    private void RegisterLocalizationHandlers()
    {
        var localization = _services.GetRequiredService<LocalizationService>();
        var settings = _services.GetRequiredService<ISettingsService>();

        Electron.IpcMain.On("hyprism:i18n:current", (_) =>
        {
            Reply("hyprism:i18n:current:reply", localization.CurrentLanguage);
        });

        Electron.IpcMain.On("hyprism:i18n:set", (args) =>
        {
            var lang = ArgsToString(args);
            if (string.IsNullOrEmpty(lang)) lang = "en-US";
            Logger.Info("IPC", $"Language change requested: {lang}");
            // Use SettingsService.SetLanguage which persists to config file
            var success = settings.SetLanguage(lang);
            Reply("hyprism:i18n:set:reply", new { success, language = success ? lang : localization.CurrentLanguage });
        });

        Electron.IpcMain.On("hyprism:i18n:languages", (_) =>
        {
            Reply("hyprism:i18n:languages:reply", LocalizationService.GetAvailableLanguages());
        });
    }
    
    // #endregion

    // #region Window Controls
    // @ipc send hyprism:window:minimize
    // @ipc send hyprism:window:maximize
    // @ipc send hyprism:window:close
    // @ipc send hyprism:browser:open

    private void RegisterWindowHandlers()
    {
        Electron.IpcMain.On("hyprism:window:minimize", (_) => GetMainWindow()?.Minimize());

        Electron.IpcMain.On("hyprism:window:maximize", async (_) =>
        {
            var win = GetMainWindow();
            if (win == null) return;
            if (await win.IsMaximizedAsync()) win.Unmaximize();
            else win.Maximize();
        });

        Electron.IpcMain.On("hyprism:window:close", (_) => GetMainWindow()?.Close());

        Electron.IpcMain.On("hyprism:browser:open", (args) =>
        {
            var url = ArgsToString(args);
            if (!string.IsNullOrEmpty(url))
                Electron.Shell.OpenExternalAsync(url);
        });
    }
    
    // #endregion

    // #region Mods
    // @ipc invoke hyprism:mods:list -> InstalledMod[]
    // @ipc invoke hyprism:mods:search -> ModSearchResult 15000
    // @ipc invoke hyprism:mods:installed -> InstalledMod[]
    // @ipc invoke hyprism:mods:uninstall -> boolean
    // @ipc invoke hyprism:mods:checkUpdates -> InstalledMod[] 30000
    // @ipc invoke hyprism:mods:install -> boolean 30000
    // @ipc invoke hyprism:mods:files -> ModFilesResult
    // @ipc invoke hyprism:mods:categories -> ModCategory[]
    // @ipc invoke hyprism:mods:installLocal -> boolean
    // @ipc invoke hyprism:mods:installBase64 -> boolean
    // @ipc send hyprism:mods:openFolder
    // @ipc invoke hyprism:mods:toggle -> boolean

    private void RegisterModHandlers()
    {
        var modService = _services.GetRequiredService<IModService>();
        var instanceService = _services.GetRequiredService<IInstanceService>();
        var config = _services.GetRequiredService<IConfigService>();

        Electron.IpcMain.On("hyprism:mods:list", (_) =>
        {
            try
            {
                var branch = config.Configuration.LauncherBranch ?? "release";
                Reply("hyprism:mods:list:reply", modService.GetInstanceInstalledMods(
                    instanceService.GetLatestInstancePath(branch)));
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods list failed: {ex.Message}");
            }
        });

        Electron.IpcMain.On("hyprism:mods:search", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                var page = root.TryGetProperty("page", out var p) ? p.GetInt32() : 0;
                var pageSize = root.TryGetProperty("pageSize", out var ps) ? ps.GetInt32() : 20;
                var sortField = root.TryGetProperty("sortField", out var sf) ? sf.GetInt32() : 1;
                var sortOrder = root.TryGetProperty("sortOrder", out var so) ? so.GetInt32() : 1;
                
                var categories = Array.Empty<string>();
                if (root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                {
                    categories = cats.EnumerateArray()
                        .Select(c => c.GetString() ?? "")
                        .Where(c => !string.IsNullOrEmpty(c))
                        .ToArray();
                }
                
                var result = await modService.SearchModsAsync(query, page, pageSize, categories, sortField, sortOrder);
                Reply("hyprism:mods:search:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods search failed: {ex.Message}");
                Reply("hyprism:mods:search:reply", new { mods = new List<object>(), totalCount = 0 });
            }
        });

        // Get installed mods for a specific instance (by branch and version)
        Electron.IpcMain.On("hyprism:mods:installed", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var branch = doc.RootElement.GetProperty("branch").GetString() ?? "release";
                var version = doc.RootElement.GetProperty("version").GetInt32();
                var instancePath = instanceService.GetInstancePath(branch, version);
                
                var mods = modService.GetInstanceInstalledMods(instancePath);
                Reply("hyprism:mods:installed:reply", mods);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods installed failed: {ex.Message}");
                Reply("hyprism:mods:installed:reply", new List<object>());
            }
        });

        // Uninstall a mod from an instance
        Electron.IpcMain.On("hyprism:mods:uninstall", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var modId = doc.RootElement.GetProperty("modId").GetString() ?? "";
                var branch = doc.RootElement.GetProperty("branch").GetString() ?? "release";
                var version = doc.RootElement.GetProperty("version").GetInt32();
                var instancePath = instanceService.GetInstancePath(branch, version);
                
                // Get current mods, remove the one with matching ID, save back
                var mods = modService.GetInstanceInstalledMods(instancePath);
                var modToRemove = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
                if (modToRemove != null)
                {
                    mods.Remove(modToRemove);
                    
                    // Delete the actual mod file if it exists
                    if (!string.IsNullOrEmpty(modToRemove.FileName))
                    {
                        var modFilePath = Path.Combine(instancePath, "Client", "mods", modToRemove.FileName);
                        if (File.Exists(modFilePath))
                        {
                            try { File.Delete(modFilePath); }
                            catch (Exception ex) { Logger.Warning("IPC", $"Failed to delete mod file: {ex.Message}"); }
                        }
                    }
                    
                    await modService.SaveInstanceModsAsync(instancePath, mods);
                    Reply("hyprism:mods:uninstall:reply", true);
                }
                else
                {
                    Reply("hyprism:mods:uninstall:reply", false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods uninstall failed: {ex.Message}");
                Reply("hyprism:mods:uninstall:reply", false);
            }
        });

        // Check for mod updates (returns mods that have updates available)
        Electron.IpcMain.On("hyprism:mods:checkUpdates", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var branch = doc.RootElement.GetProperty("branch").GetString() ?? "release";
                var version = doc.RootElement.GetProperty("version").GetInt32();
                var instancePath = instanceService.GetInstancePath(branch, version);
                
                var updates = await modService.CheckInstanceModUpdatesAsync(instancePath);
                Reply("hyprism:mods:checkUpdates:reply", updates);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods check updates failed: {ex.Message}");
                Reply("hyprism:mods:checkUpdates:reply", new List<object>());
            }
        });
        
        // Install a mod from CurseForge by modId and fileId
        Electron.IpcMain.On("hyprism:mods:install", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";
                var fileId = root.GetProperty("fileId").GetString() ?? "";
                var branch = root.TryGetProperty("branch", out var b) ? b.GetString() ?? "release" : "release";
                var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
                
                string instancePath;
                if (version > 0)
                    instancePath = instanceService.GetInstancePath(branch, version);
                else
                    instancePath = instanceService.GetLatestInstancePath(branch);
                
                var success = await modService.InstallModFileToInstanceAsync(modId, fileId, instancePath);
                Reply("hyprism:mods:install:reply", success);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods install failed: {ex.Message}");
                Reply("hyprism:mods:install:reply", false);
            }
        });
        
        // Get available files for a mod
        Electron.IpcMain.On("hyprism:mods:files", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";
                var page = root.TryGetProperty("page", out var p) ? p.GetInt32() : 0;
                var pageSize = root.TryGetProperty("pageSize", out var ps) ? ps.GetInt32() : 20;
                
                var result = await modService.GetModFilesAsync(modId, page, pageSize);
                Reply("hyprism:mods:files:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods files failed: {ex.Message}");
                Reply("hyprism:mods:files:reply", new { files = new List<object>(), totalCount = 0 });
            }
        });
        
        // Get mod categories
        Electron.IpcMain.On("hyprism:mods:categories", async (_) =>
        {
            try
            {
                var categories = await modService.GetModCategoriesAsync();
                Reply("hyprism:mods:categories:reply", categories);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods categories failed: {ex.Message}");
                Reply("hyprism:mods:categories:reply", new List<object>());
            }
        });
        
        // Install mod from local file path
        Electron.IpcMain.On("hyprism:mods:installLocal", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sourcePath = root.GetProperty("sourcePath").GetString() ?? "";
                var branch = root.TryGetProperty("branch", out var b) ? b.GetString() ?? "release" : "release";
                var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
                
                string instancePath;
                if (version > 0)
                    instancePath = instanceService.GetInstancePath(branch, version);
                else
                    instancePath = instanceService.GetLatestInstancePath(branch);
                
                var success = await modService.InstallLocalModFile(sourcePath, instancePath);
                Reply("hyprism:mods:installLocal:reply", success);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods install local failed: {ex.Message}");
                Reply("hyprism:mods:installLocal:reply", false);
            }
        });
        
        // Install mod from base64-encoded content
        Electron.IpcMain.On("hyprism:mods:installBase64", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var fileName = root.GetProperty("fileName").GetString() ?? "";
                var base64Content = root.GetProperty("base64Content").GetString() ?? "";
                var branch = root.TryGetProperty("branch", out var b) ? b.GetString() ?? "release" : "release";
                var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
                
                string instancePath;
                if (version > 0)
                    instancePath = instanceService.GetInstancePath(branch, version);
                else
                    instancePath = instanceService.GetLatestInstancePath(branch);
                
                var success = await modService.InstallModFromBase64(fileName, base64Content, instancePath);
                Reply("hyprism:mods:installBase64:reply", success);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods install base64 failed: {ex.Message}");
                Reply("hyprism:mods:installBase64:reply", false);
            }
        });
        
        // Open the mods folder for an instance
        Electron.IpcMain.On("hyprism:mods:openFolder", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var branch = root.TryGetProperty("branch", out var b) ? b.GetString() ?? "release" : "release";
                var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
                
                string instancePath;
                if (version > 0)
                    instancePath = instanceService.GetInstancePath(branch, version);
                else
                    instancePath = instanceService.GetLatestInstancePath(branch);
                
                var modsPath = Path.Combine(instancePath, "Client", "mods");
                Directory.CreateDirectory(modsPath);
                Electron.Shell.OpenPathAsync(modsPath);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Open mods folder failed: {ex.Message}");
            }
        });
        
        // Toggle mod enabled/disabled (renames .jar <-> .jar.disabled)
        Electron.IpcMain.On("hyprism:mods:toggle", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();
                var instancePath = instanceService.GetInstancePath(branch, version);
                
                var mods = modService.GetInstanceInstalledMods(instancePath);
                var mod = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
                if (mod == null || string.IsNullOrEmpty(mod.FileName))
                {
                    Reply("hyprism:mods:toggle:reply", false);
                    return;
                }
                
                var modsDir = Path.Combine(instancePath, "Client", "mods");
                var currentPath = Path.Combine(modsDir, mod.FileName);
                
                if (mod.Enabled)
                {
                    // Disable: rename file.jar -> file.jar.disabled
                    var disabledPath = currentPath + ".disabled";
                    if (File.Exists(currentPath))
                    {
                        File.Move(currentPath, disabledPath);
                        mod.FileName = mod.FileName + ".disabled";
                        mod.Enabled = false;
                        Logger.Info("IPC", $"Disabled mod: {mod.Name}");
                    }
                }
                else
                {
                    // Enable: rename file.jar.disabled -> file.jar
                    if (mod.FileName.EndsWith(".disabled") && File.Exists(currentPath))
                    {
                        var enabledPath = currentPath[..^".disabled".Length];
                        File.Move(currentPath, enabledPath);
                        mod.FileName = mod.FileName[..^".disabled".Length];
                        mod.Enabled = true;
                        Logger.Info("IPC", $"Enabled mod: {mod.Name}");
                    }
                }
                
                await modService.SaveInstanceModsAsync(instancePath, mods);
                Reply("hyprism:mods:toggle:reply", true);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods toggle failed: {ex.Message}");
                Reply("hyprism:mods:toggle:reply", false);
            }
        });
    }

    // #region System Info
    // @ipc invoke hyprism:system:gpuAdapters -> GpuAdapterInfo[]

    private void RegisterSystemHandlers()
    {
        var gpuService = _services.GetRequiredService<GpuDetectionService>();

        Electron.IpcMain.On("hyprism:system:gpuAdapters", (_) =>
        {
            try
            {
                var adapters = gpuService.GetAdapters();
                Reply("hyprism:system:gpuAdapters:reply", adapters);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get GPU adapters: {ex.Message}");
                Reply("hyprism:system:gpuAdapters:reply", new List<object>());
            }
        });
    }

    // #endregion

    // #region Console (Electron renderer → .NET Logger)
    // @ipc send hyprism:console:log
    // @ipc send hyprism:console:warn
    // @ipc send hyprism:console:error

    private void RegisterConsoleHandlers()
    {
        Electron.IpcMain.On("hyprism:console:log", (args) =>
            Logger.Info("Renderer", ArgsToString(args)));

        Electron.IpcMain.On("hyprism:console:warn", (args) =>
            Logger.Warning("Renderer", ArgsToString(args)));

        Electron.IpcMain.On("hyprism:console:error", (args) =>
            Logger.Error("Renderer", ArgsToString(args)));
    }

    // #endregion

    // #region File Dialog
    // @ipc invoke hyprism:file:browseFolder -> string | null
    // @ipc invoke hyprism:file:browseModFiles -> string[]
    // @ipc invoke hyprism:mods:exportToFolder -> string
    // @ipc invoke hyprism:mods:importList -> number
    // @ipc invoke hyprism:settings:launcherPath -> string
    // @ipc invoke hyprism:settings:defaultInstanceDir -> string

    private void RegisterFileDialogHandlers()
    {
        var fileDialog = _services.GetRequiredService<IFileDialogService>();
        var appPath = _services.GetRequiredService<AppPathConfiguration>();
        var config = _services.GetRequiredService<IConfigService>();
        var instanceService = _services.GetRequiredService<IInstanceService>();
        var modService = _services.GetRequiredService<IModService>();

        // Browse mod files dialog (jar, zip, json)
        Electron.IpcMain.On("hyprism:file:browseModFiles", async (_) =>
        {
            try
            {
                var files = await fileDialog.BrowseModFilesAsync();
                Reply("hyprism:file:browseModFiles:reply", files ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to browse mod files: {ex.Message}");
                Reply("hyprism:file:browseModFiles:reply", Array.Empty<string>());
            }
        });

        // Export mods to folder (modlist JSON or zip)
        Electron.IpcMain.On("hyprism:mods:exportToFolder", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();
                var exportPath = root.GetProperty("exportPath").GetString() ?? "";
                var exportType = root.TryGetProperty("exportType", out var et) ? et.GetString() ?? "modlist" : "modlist";

                if (string.IsNullOrEmpty(exportPath))
                {
                    Reply("hyprism:mods:exportToFolder:reply", "");
                    return;
                }

                var instancePath = instanceService.GetInstancePath(branch, version);
                var mods = modService.GetInstanceInstalledMods(instancePath);

                if (mods.Count == 0)
                {
                    Reply("hyprism:mods:exportToFolder:reply", "");
                    return;
                }

                // Save last export path to config
                config.Configuration.LastExportPath = exportPath;
                config.SaveConfig();

                if (exportType == "zip")
                {
                    // Zip the mods folder
                    var modsDir = Path.Combine(instancePath, "Client", "mods");
                    if (!Directory.Exists(modsDir))
                    {
                        Reply("hyprism:mods:exportToFolder:reply", "");
                        return;
                    }

                    var zipName = $"HyPrism-Mods-{branch}-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
                    var zipPath = Path.Combine(exportPath, zipName);
                    System.IO.Compression.ZipFile.CreateFromDirectory(modsDir, zipPath);
                    Logger.Success("IPC", $"Exported mods zip to: {zipPath}");
                    Reply("hyprism:mods:exportToFolder:reply", zipPath);
                }
                else
                {
                    // Export as mod list JSON
                    var modList = mods
                        .Where(m => !string.IsNullOrEmpty(m.CurseForgeId))
                        .Select(m => new ModListEntry
                        {
                            CurseForgeId = m.CurseForgeId,
                            FileId = m.FileId,
                            Name = m.Name,
                            Version = m.Version
                        })
                        .ToList();

                    if (modList.Count == 0)
                    {
                        Reply("hyprism:mods:exportToFolder:reply", "");
                        return;
                    }

                    var fileName = $"HyPrism-ModList-{branch}-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                    var filePath = Path.Combine(exportPath, fileName);
                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(modList, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, jsonContent);
                    Logger.Success("IPC", $"Exported mod list to: {filePath}");
                    Reply("hyprism:mods:exportToFolder:reply", filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to export mods: {ex.Message}");
                Reply("hyprism:mods:exportToFolder:reply", "");
            }
        });

        // Import mod list from JSON file
        Electron.IpcMain.On("hyprism:mods:importList", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var filePath = root.GetProperty("filePath").GetString() ?? "";
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Reply("hyprism:mods:importList:reply", 0);
                    return;
                }

                var instancePath = instanceService.GetInstancePath(branch, version);
                var content = await File.ReadAllTextAsync(filePath);
                var modList = System.Text.Json.JsonSerializer.Deserialize<List<ModListEntry>>(content) ?? new();
                var successCount = 0;

                foreach (var entry in modList)
                {
                    if (string.IsNullOrEmpty(entry.CurseForgeId)) continue;
                    try
                    {
                        var fileId = entry.FileId ?? "";
                        var success = await modService.InstallModFileToInstanceAsync(entry.CurseForgeId, fileId, instancePath);
                        if (success) successCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("IPC", $"Failed to import mod {entry.Name}: {ex.Message}");
                    }
                }

                Logger.Success("IPC", $"Imported {successCount}/{modList.Count} mods from list");
                Reply("hyprism:mods:importList:reply", successCount);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to import mod list: {ex.Message}");
                Reply("hyprism:mods:importList:reply", 0);
            }
        });

        // Browse folder dialog
        Electron.IpcMain.On("hyprism:file:browseFolder", async (args) =>
        {
            try
            {
                var initialPath = ArgsToString(args);
                var selected = await fileDialog.BrowseFolderAsync(string.IsNullOrEmpty(initialPath) ? null : initialPath);
                Reply("hyprism:file:browseFolder:reply", selected ?? "");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to browse folder: {ex.Message}");
                Reply("hyprism:file:browseFolder:reply", "");
            }
        });

        // Get launcher folder path (app data path)
        Electron.IpcMain.On("hyprism:settings:launcherPath", (_) =>
        {
            Reply("hyprism:settings:launcherPath:reply", appPath.AppDir);
        });

        // Get default instance directory
        Electron.IpcMain.On("hyprism:settings:defaultInstanceDir", (_) =>
        {
            var defaultDir = Path.Combine(appPath.AppDir, "game");
            Reply("hyprism:settings:defaultInstanceDir:reply", defaultDir);
        });
        
        // Set instance directory
        Electron.IpcMain.On("hyprism:settings:setInstanceDir", async (args) =>
        {
            try
            {
                var path = ArgsToString(args);
                var result = await config.SetInstanceDirectoryAsync(path);
                Reply("hyprism:settings:setInstanceDir:reply", new { success = result != null, path = result ?? "" });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to set instance directory: {ex.Message}");
                Reply("hyprism:settings:setInstanceDir:reply", new { success = false, path = "" });
            }
        });
        
        // Set launcher data directory (in config)
        Electron.IpcMain.On("hyprism:settings:setLauncherDataDir", (args) =>
        {
            try
            {
                var path = ArgsToString(args);
                var cfg = config.Configuration;
                cfg.LauncherDataDirectory = path;
                config.SaveConfig();
                Reply("hyprism:settings:setLauncherDataDir:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to set launcher data directory: {ex.Message}");
                Reply("hyprism:settings:setLauncherDataDir:reply", new { success = false, error = ex.Message });
            }
        });
    }

    // #endregion
}
