using System;
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
/// @type SettingsSnapshot { language: string; musicEnabled: boolean; launcherBranch: string; closeAfterLaunch: boolean; showDiscordAnnouncements: boolean; disableNews: boolean; backgroundMode: string; availableBackgrounds: string[]; accentColor: string; hasCompletedOnboarding: boolean; onlineMode: boolean; authDomain: string; dataDirectory: string; launchOnStartup?: boolean; minimizeToTray?: boolean; animations?: boolean; transparency?: boolean; resolution?: string; ramMb?: number; sound?: boolean; closeOnLaunch?: boolean; developerMode?: boolean; verboseLogging?: boolean; preRelease?: boolean; [key: string]: unknown; }
/// @type ModItem { id: string; name: string; description?: string; version?: string; author?: string; iconUrl?: string; isInstalled: boolean; featured?: boolean; downloads?: number; }
/// @type ModSearchResult { items: ModItem[]; totalCount: number; }
/// @type AppConfig { language: string; dataDirectory: string; [key: string]: unknown; }
/// @type InstalledInstance { branch: string; version: number; path: string; hasUserData: boolean; userDataSize: number; totalSize: number; }
/// @type LanguageInfo { code: string; name: string; }
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
        RegisterNewsHandlers();
        RegisterProfileHandlers();
        RegisterSettingsHandlers();
        RegisterLocalizationHandlers();
        RegisterWindowHandlers();
        RegisterModHandlers();
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

        Electron.IpcMain.On("hyprism:game:launch", async (_) =>
        {
            // First check if game is already running
            if (gameProcessService.IsGameRunning())
            {
                Logger.Warning("IPC", "Game launch request ignored - game already running");
                return;
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
                Logger.Info("IPC", $"Returning {instances.Count} installed instances");
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
                var isRunning = gameProcessService.IsGameRunning();
                Logger.Info("IPC", $"Game running check: {isRunning}");
                Reply("hyprism:game:isRunning:reply", isRunning);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to check game running: {ex.Message}");
                Reply("hyprism:game:isRunning:reply", false);
            }
        });

        Electron.IpcMain.On("hyprism:game:versions", async (args) =>
        {
            try
            {
                // Parse branch from args, default to current config branch
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
            Reply("hyprism:settings:get:reply", new
            {
                language = settings.GetLanguage(),
                musicEnabled = settings.GetMusicEnabled(),
                launcherBranch = settings.GetLauncherBranch(),
                closeAfterLaunch = settings.GetCloseAfterLaunch(),
                showDiscordAnnouncements = settings.GetShowDiscordAnnouncements(),
                disableNews = settings.GetDisableNews(),
                backgroundMode = settings.GetBackgroundMode(),
                availableBackgrounds = settings.GetAvailableBackgrounds(),
                accentColor = settings.GetAccentColor(),
                hasCompletedOnboarding = settings.GetHasCompletedOnboarding(),
                onlineMode = settings.GetOnlineMode(),
                authDomain = settings.GetAuthDomain(),
                dataDirectory = settings.GetLauncherDataDirectory()
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
            case "closeAfterLaunch": s.SetCloseAfterLaunch(val.GetBoolean()); break;
            case "showDiscordAnnouncements": s.SetShowDiscordAnnouncements(val.GetBoolean()); break;
            case "disableNews": s.SetDisableNews(val.GetBoolean()); break;
            case "backgroundMode": s.SetBackgroundMode(val.GetString() ?? "default"); break;
            case "accentColor": s.SetAccentColor(val.GetString() ?? "#7C5CFC"); break;
            case "onlineMode": s.SetOnlineMode(val.GetBoolean()); break;
            case "authDomain": s.SetAuthDomain(val.GetString() ?? ""); break;
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
