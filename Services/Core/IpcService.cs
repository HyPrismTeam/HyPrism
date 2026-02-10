using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection;
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
/// @type Profile { id: string; name: string; avatar?: string; }
/// @type ProfileSnapshot { nick: string; uuid: string; avatarPath?: string; }
/// @type SettingsSnapshot { language: string; musicEnabled: boolean; launcherBranch: string; closeAfterLaunch: boolean; showDiscordAnnouncements: boolean; disableNews: boolean; backgroundMode: string; availableBackgrounds: string[]; accentColor: string; hasCompletedOnboarding: boolean; onlineMode: boolean; authDomain: string; dataDirectory: string; gpuPreference?: string; launchOnStartup?: boolean; minimizeToTray?: boolean; animations?: boolean; transparency?: boolean; resolution?: string; ramMb?: number; sound?: boolean; closeOnLaunch?: boolean; developerMode?: boolean; verboseLogging?: boolean; preRelease?: boolean; [key: string]: unknown; }
/// @type ModItem { id: string; name: string; description?: string; version?: string; author?: string; iconUrl?: string; isInstalled: boolean; featured?: boolean; downloads?: number; }
/// @type ModSearchResult { items: ModItem[]; totalCount: number; }
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
        RegisterSettingsHandlers();
        RegisterLocalizationHandlers();
        RegisterWindowHandlers();
        RegisterModHandlers();
        RegisterSystemHandlers();
        RegisterConsoleHandlers();

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
                            configService.Configuration.LauncherBranch = branchEl.GetString() ?? "release";
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
                var modsPath = Path.Combine(instancePath, "Mods");
                
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

    private void RegisterProfileHandlers()
    {
        var profileService = _services.GetRequiredService<IProfileService>();

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
            Reply("hyprism:profile:list:reply", profileService.GetProfiles());
        });

        Electron.IpcMain.On("hyprism:profile:switch", (args) =>
        {
            try
            {
                var profileId = ArgsToString(args);
                Reply("hyprism:profile:switch:reply", new { success = profileService.SwitchProfile(profileId) });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Profile switch failed: {ex.Message}");
            }
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
                animatedGlassEffects = settings.GetAnimatedGlassEffects()
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
            case "animatedGlassEffects": s.SetAnimatedGlassEffects(val.GetBoolean()); break;
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

        Electron.IpcMain.On("hyprism:i18n:get", (args) =>
        {
            var code = ArgsToString(args);
            Reply("hyprism:i18n:get:reply", localization.GetAllTranslations(string.IsNullOrEmpty(code) ? null : code));
        });

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
    // @ipc invoke hyprism:mods:list -> ModItem[]
    // @ipc invoke hyprism:mods:search -> ModSearchResult

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
                var query = ArgsToString(args);
                Reply("hyprism:mods:search:reply",
                    await modService.SearchModsAsync(query, 0, 20, Array.Empty<string>(), 1, 1));
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods search failed: {ex.Message}");
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
}
