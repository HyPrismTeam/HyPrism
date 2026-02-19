using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.Game.Mod;
using HyPrism.Services.Game.Sources;
using HyPrism.Services.Game.Version;
using HyPrism.Services.User;

namespace HyPrism.Services.Core.Ipc;

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
/// @type NewsItem { title: string; excerpt?: string; url?: string; date?: string; publishedAt?: string; author?: string; imageUrl?: string; source?: 'hytale' | 'hyprism'; }
/// @type Profile { id: string; name: string; uuid?: string; isOfficial?: boolean; avatar?: string; folderName?: string; }
/// @type HytaleAuthStatus { loggedIn: boolean; username?: string; uuid?: string; error?: string; errorType?: string; }
/// @type ProfileSnapshot { nick: string; uuid: string; avatarPath?: string; }
/// @type SettingsSnapshot { language: string; musicEnabled: boolean; launcherBranch: string; versionType: string; selectedVersion: number; closeAfterLaunch: boolean; launchAfterDownload: boolean; showDiscordAnnouncements: boolean; disableNews: boolean; backgroundMode: string; availableBackgrounds: string[]; accentColor: string; hasCompletedOnboarding: boolean; onlineMode: boolean; authDomain: string; javaArguments?: string; useCustomJava?: boolean; customJavaPath?: string; systemMemoryMb?: number; dataDirectory: string; instanceDirectory: string; gpuPreference?: string; gameEnvironmentVariables?: Record<string, string>; useDualAuth?: boolean; showAlphaMods: boolean; launcherVersion: string; launchOnStartup?: boolean; minimizeToTray?: boolean; animations?: boolean; transparency?: boolean; resolution?: string; ramMb?: number; sound?: boolean; closeOnLaunch?: boolean; developerMode?: boolean; verboseLogging?: boolean; preRelease?: boolean; [key: string]: unknown; }
/// @type MirrorInfo { id: string; name: string; description?: string; priority: number; enabled: boolean; sourceType: string; hostname: string; }
/// @type MirrorSpeedTestResult { mirrorId: string; mirrorUrl: string; mirrorName: string; pingMs: number; speedMBps: number; isAvailable: boolean; testedAt: string; }
/// @type ModScreenshot { id: number; title: string; thumbnailUrl: string; url: string; }
/// @type ModInfo { id: string; name: string; slug: string; summary: string; author: string; downloadCount: number; iconUrl: string; thumbnailUrl: string; categories: string[]; dateUpdated: string; latestFileId: string; screenshots: ModScreenshot[]; }
/// @type ModSearchResult { mods: ModInfo[]; totalCount: number; }
/// @type ModFileInfo { id: string; modId: string; fileName: string; displayName: string; downloadUrl: string; fileLength: number; fileDate: string; releaseType: number; gameVersions: string[]; downloadCount: number; }
/// @type ModFilesResult { files: ModFileInfo[]; totalCount: number; }
/// @type ModCategory { id: number; name: string; slug: string; }
/// @type InstalledMod { id: string; name: string; slug?: string; version?: string; fileId?: string; fileName?: string; enabled: boolean; author?: string; description?: string; iconUrl?: string; curseForgeId?: string; fileDate?: string; releaseType?: number; latestFileId?: string; latestVersion?: string; screenshots?: ModScreenshot[]; }
/// @type SaveInfo { name: string; previewPath?: string; lastModified?: string; sizeBytes?: number; }
/// @type AppConfig { language: string; dataDirectory: string; [key: string]: unknown; }
/// @type InstanceValidationDetails { hasExecutable: boolean; hasAssets: boolean; hasLibraries: boolean; hasConfig: boolean; missingComponents: string[]; errorMessage?: string; }
/// @type InstalledInstance { id: string; branch: string; version: number; path: string; hasUserData: boolean; userDataSize: number; totalSize: number; isValid: boolean; validationStatus?: 'Valid' | 'NotInstalled' | 'Corrupted' | 'Unknown'; validationDetails?: InstanceValidationDetails; customName?: string; }
/// @type InstanceInfo { id: string; name: string; branch: string; version: number; isInstalled: boolean; }
/// @type LanguageInfo { code: string; name: string; }
/// @type GpuAdapterInfo { name: string; vendor: string; type: string; }
/// @type VersionInfo { version: number; source: 'Official' | 'Mirror'; isLatest: boolean; }
/// @type VersionListResponse { versions: VersionInfo[]; hasOfficialAccount: boolean; officialSourceAvailable: boolean; }
/// @type LauncherUpdateInfo { currentVersion: string; latestVersion: string; changelog?: string; downloadUrl?: string; assetName?: string; releaseUrl?: string; isBeta?: boolean; }
/// @type LauncherUpdateProgress { stage: string; progress: number; message: string; downloadedBytes?: number; totalBytes?: number; downloadedFilePath?: string; hasDownloadedFile?: boolean; }
public class IpcService
{
    private readonly IServiceProvider _services;

    private static int _hasRegisteredAll;

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

    private static void Reply(string channel, object? data)
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

    private static int GetSystemMemoryMb()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memoryStatus = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(memoryStatus) && memoryStatus.ullTotalPhys > 0)
                {
                    return (int)(memoryStatus.ullTotalPhys / (1024 * 1024));
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const string memInfoPath = "/proc/meminfo";
                if (File.Exists(memInfoPath))
                {
                    var memTotalLine = File.ReadLines(memInfoPath).FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(memTotalLine))
                    {
                        var parts = memTotalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var kb) && kb > 0)
                        {
                            return (int)(kb / 1024);
                        }
                    }
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/sysctl",
                    Arguments = "-n hw.memsize",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(2000);
                    if (process.ExitCode == 0 && long.TryParse(output, out var bytes) && bytes > 0)
                    {
                        return (int)(bytes / (1024 * 1024));
                    }
                }
            }
        }
        catch
        {
        }

        var fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (fallback > 0)
        {
            return (int)Math.Max(1024, fallback / (1024 * 1024));
        }

        return 8192;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    public void RegisterAll()
    {
        if (Interlocked.Exchange(ref _hasRegisteredAll, 1) == 1)
        {
            Logger.Warning("IPC", "RegisterAll called more than once; skipping duplicate IPC handler registration");
            return;
        }

        Logger.Info("IPC", "Registering IPC handlers...");

        RegisterConfigHandlers();
        RegisterGameHandlers();
        RegisterInstanceHandlers();
        RegisterNewsHandlers();
        RegisterProfileHandlers();
        RegisterAuthHandlers();
        RegisterSettingsHandlers();
        RegisterUpdateHandlers();
        RegisterLocalizationHandlers();
        RegisterWindowHandlers();
        RegisterModHandlers();
        RegisterSystemHandlers();
        RegisterConsoleHandlers();
        RegisterFileDialogHandlers();

        Logger.Success("IPC", "All IPC handlers registered");
    }

    // #region Launcher Update
    // @ipc invoke hyprism:update:check -> { success: boolean }
    // @ipc invoke hyprism:update:install -> boolean 300000
    // @ipc event hyprism:update:available -> LauncherUpdateInfo
    // @ipc event hyprism:update:progress -> LauncherUpdateProgress

    private void RegisterUpdateHandlers()
    {
        var updateService = _services.GetRequiredService<IUpdateService>();

        // Forward backend update notifications to renderer
        updateService.LauncherUpdateAvailable += (info) =>
        {
            try { Reply("hyprism:update:available", info); } catch { /* swallow */ }
        };

        updateService.LauncherUpdateProgress += (progress) =>
        {
            try { Reply("hyprism:update:progress", progress); } catch { /* swallow */ }
        };

        // Explicit check (useful for manual refresh or debugging)
        Electron.IpcMain.On("hyprism:update:check", async (_) =>
        {
            try
            {
                await updateService.CheckForLauncherUpdatesAsync();
                Reply("hyprism:update:check:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Update check failed: {ex.Message}");
                Reply("hyprism:update:check:reply", new { success = false, error = ex.Message });
            }
        });

        // Install update (will usually terminate the current process after starting the replacement script)
        Electron.IpcMain.On("hyprism:update:install", async (_) =>
        {
            try
            {
                var ok = await updateService.UpdateAsync(null);
                Reply("hyprism:update:install:reply", ok);

                if (ok)
                {
                    // Give IPC a moment to flush, then exit. The updater script will restart the app.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(750);
                        try { Electron.App.Exit(); } catch { Environment.Exit(0); }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Update install failed: {ex.Message}");
                Reply("hyprism:update:install:reply", false);
            }
        });
    }

    // #endregion

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
    // @ipc invoke hyprism:game:stop -> boolean
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
            
            // Default behavior comes from persisted config.
            var launchAfterDownload = configService.Configuration.LaunchAfterDownload;

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
                            #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
                            configService.Configuration.VersionType = branchValue;
                            #pragma warning restore CS0618
                            configService.Configuration.LauncherBranch = branchValue;
                        }
                        if (data.TryGetValue("version", out var versionEl))
                        {
                            #pragma warning disable CS0618 // Backward compatibility: SelectedVersion kept for migration
                            configService.Configuration.SelectedVersion = versionEl.GetInt32();
                            #pragma warning restore CS0618
                        }

                        if (data.TryGetValue("instanceId", out var instanceIdEl))
                        {
                            var instanceId = instanceIdEl.GetString();
                            if (!string.IsNullOrWhiteSpace(instanceId))
                            {
                                instanceService.SetSelectedInstance(instanceId);

                                // Ensure launch config follows selected instance exactly.
                                var selected = instanceService.FindInstanceById(instanceId);
                                if (selected != null)
                                {
                                    #pragma warning disable CS0618 // Backward compatibility: VersionType/SelectedVersion kept for migration
                                    configService.Configuration.VersionType = selected.Branch;
                                    configService.Configuration.SelectedVersion = selected.Version;
                                    #pragma warning restore CS0618
                                }
                            }
                        }

                        if (data.TryGetValue("launchAfterDownload", out var launchAfterEl) && launchAfterEl.ValueKind == JsonValueKind.True)
                        {
                            launchAfterDownload = true;
                        }
                        else if (data.TryGetValue("launchAfterDownload", out launchAfterEl) && launchAfterEl.ValueKind == JsonValueKind.False)
                        {
                            launchAfterDownload = false;
                        }
                    }
                }
                catch { /* ignore parsing errors, use current config */ }
            }
            
            Logger.Info("IPC", "Game launch requested");
            try
            {
                var result = await gameSession.DownloadAndLaunchAsync(() => launchAfterDownload);

                if (result.Cancelled || string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    // Cancellation is a normal operation — just reset state, no error.
                    progressService.ReportGameStateChanged("stopped", 0);
                    return;
                }

                // Download-only (no launch) completed successfully
                if (result.Success && !launchAfterDownload)
                {
                    progressService.ReportGameStateChanged("stopped", 0);
                    return;
                }

                if (!result.Success)
                {
                    var technical = result.Error ?? "Unknown download/install error";
                    progressService.ReportError("download", "Failed to install game", technical);
                    progressService.ReportGameStateChanged("stopped", 1);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Game launch failed: {ex.Message}");
                progressService.ReportError("download", "Failed to install game", ex.ToString());
                progressService.ReportGameStateChanged("stopped", 1);
            }
        });

        Electron.IpcMain.On("hyprism:game:cancel", (_) =>
        {
            Logger.Info("IPC", "Game download cancel requested");
            gameSession.CancelDownload();
        });

        Electron.IpcMain.On("hyprism:game:stop", (_) =>
        {
            try
            {
                var stopped = gameProcessService.ExitGame();
                Logger.Info("IPC", stopped ? "Game stop requested and process terminated" : "Game stop requested but no running process found");
                Reply("hyprism:game:stop:reply", stopped);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Game stop failed: {ex.Message}");
                Reply("hyprism:game:stop:reply", false);
            }
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
            catch
            {
                Reply("hyprism:game:isRunning:reply", false);
            }
        });

        Electron.IpcMain.On("hyprism:game:versions", async (args) =>
        {
            try
            {
                #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
                string branch = configService.Configuration.VersionType ?? "release";
                #pragma warning restore CS0618
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

        // Get versions with source information (official vs mirror)
        // @ipc invoke hyprism:game:versionsWithSources -> VersionListResponse
        Electron.IpcMain.On("hyprism:game:versionsWithSources", async (args) =>
        {
            try
            {
                #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
                string branch = configService.Configuration.VersionType ?? "release";
                #pragma warning restore CS0618
                if (args != null)
                {
                    var json = ArgsToJson(args);
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
                    if (data != null && data.TryGetValue("branch", out var b) && !string.IsNullOrEmpty(b))
                    {
                        branch = b;
                    }
                }
                
                var response = await versionService.GetVersionListWithSourcesAsync(branch);
                Logger.Info("IPC", $"Returning {response.Versions.Count} versions with sources for branch {branch} (official={response.OfficialSourceAvailable})");
                Reply("hyprism:game:versionsWithSources:reply", response);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get versions with sources: {ex.Message}");
                Reply("hyprism:game:versionsWithSources:reply", new { versions = new List<object>(), hasOfficialAccount = false, officialSourceAvailable = false });
            }
        });
    }
    // #endregion

    // #region Instance Management
    // @ipc invoke hyprism:instance:create -> InstanceInfo | null
    // @ipc invoke hyprism:instance:delete -> boolean
    // @ipc send hyprism:instance:openFolder
    // @ipc send hyprism:instance:openModsFolder
    // @ipc invoke hyprism:instance:export -> string
    // @ipc invoke hyprism:instance:import -> boolean
    // @ipc invoke hyprism:instance:saves -> SaveInfo[]
    // @ipc send hyprism:instance:openSaveFolder
    // @ipc invoke hyprism:instance:getIcon -> string | null
    // @ipc invoke hyprism:instance:select -> boolean
    // @ipc invoke hyprism:instance:getSelected -> InstanceInfo | null
    // @ipc invoke hyprism:instance:list -> InstanceInfo[]

    private void RegisterInstanceHandlers()
    {
        var instanceService = _services.GetRequiredService<IInstanceService>();
        var fileService = _services.GetRequiredService<IFileService>();

        // Create an instance with generated ID
        Electron.IpcMain.On("hyprism:instance:create", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var customName = data?.ContainsKey("customName") == true ? data["customName"].GetString() : null;
                var isLatest = data?.ContainsKey("isLatest") == true && data["isLatest"].GetBoolean();

                // Create the instance with generated ID
                var meta = instanceService.CreateInstanceMeta(branch, version, customName, isLatest);
                
                Logger.Success("IPC", $"Created instance {meta.Id} ({meta.Name})");
                Reply("hyprism:instance:create:reply", new {
                    id = meta.Id,
                    name = meta.Name,
                    branch = meta.Branch,
                    version = meta.Version
                });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to create instance: {ex.Message}");
                Reply("hyprism:instance:create:reply", null);
            }
        });

        // Select an instance by ID
        Electron.IpcMain.On("hyprism:instance:select", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?["id"].GetString() ?? "";
                
                if (string.IsNullOrEmpty(instanceId))
                {
                    Reply("hyprism:instance:select:reply", false);
                    return;
                }
                
                instanceService.SetSelectedInstance(instanceId);
                Logger.Info("IPC", $"Selected instance: {instanceId}");
                Reply("hyprism:instance:select:reply", true);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to select instance: {ex.Message}");
                Reply("hyprism:instance:select:reply", false);
            }
        });

        // Get selected instance
        Electron.IpcMain.On("hyprism:instance:getSelected", (_) =>
        {
            try
            {
                var selected = instanceService.GetSelectedInstance();
                Reply("hyprism:instance:getSelected:reply", selected != null ? new {
                    id = selected.Id,
                    name = selected.Name,
                    branch = selected.Branch,
                    version = selected.Version,
                    isInstalled = selected.IsInstalled
                } : null);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get selected instance: {ex.Message}");
                Reply("hyprism:instance:getSelected:reply", null);
            }
        });

        // List all instances from config
        Electron.IpcMain.On("hyprism:instance:list", (_) =>
        {
            try
            {
                instanceService.SyncInstancesWithConfig();
                var config = _services.GetRequiredService<IConfigService>().Configuration;
                var instances = config.Instances?.Select(i => {
                    // Check installation status for each instance
                    var instancePath = instanceService.GetInstancePathById(i.Id);
                    bool isInstalled = false;
                    if (!string.IsNullOrEmpty(instancePath))
                    {
                        isInstalled = instanceService.IsClientPresent(instancePath);
                    }
                    return (object)new {
                        id = i.Id,
                        name = i.Name,
                        branch = i.Branch,
                        version = i.Version,
                        isInstalled = isInstalled
                    };
                }).ToList() ?? new List<object>();
                
                Reply("hyprism:instance:list:reply", instances);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to list instances: {ex.Message}");
                Reply("hyprism:instance:list:reply", new List<object>());
            }
        });

        // Delete an instance
        Electron.IpcMain.On("hyprism:instance:delete", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?.TryGetValue("instanceId", out var idElement) == true ? idElement.GetString() : null;

                bool result;
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    result = instanceService.DeleteGameById(instanceId);
                    Logger.Info("IPC", $"Deleted instance by ID {instanceId}: {result}");
                }
                else
                {
                    var branch = data?["branch"].GetString() ?? "release";
                    var version = data?["version"].GetInt32() ?? 0;
                    result = instanceService.DeleteGame(branch, version);
                    Logger.Info("IPC", $"Deleted instance {branch}/{version}: {result}");
                }

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
                var instanceId = data?["instanceId"].GetString();
                
                if (string.IsNullOrEmpty(instanceId))
                {
                    Logger.Warning("IPC", "openFolder: instanceId is required");
                    return;
                }
                
                var path = instanceService.GetInstancePathById(instanceId);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    fileService.OpenFolder(path);
                    Logger.Info("IPC", $"Opened folder: {path}");
                }
                else
                {
                    Logger.Warning("IPC", $"Instance folder not found for id: {instanceId}");
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
                
                string? instancePath = null;
                
                // Try instanceId first (new method)
                if (data?.TryGetValue("instanceId", out var idElement) == true)
                {
                    var instanceId = idElement.GetString();
                    if (!string.IsNullOrEmpty(instanceId))
                    {
                        instancePath = instanceService.GetInstancePathById(instanceId);
                    }
                }
                
                // Fall back to branch/version (for ModManager compatibility)
                if (string.IsNullOrEmpty(instancePath))
                {
                    var branch = data?["branch"].GetString() ?? "release";
                    var version = data?["version"].GetInt32() ?? 0;
                    instancePath = instanceService.GetInstancePath(branch, version);
                }
                
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", "openModsFolder: Could not find instance path");
                    return;
                }
                
                var modsPath = Path.Combine(instancePath, "UserData", "Mods");
                
                ModService.EnsureModsDirectory(modsPath);
                
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
                var fileDialog = _services.GetRequiredService<IFileDialogService>();
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?.TryGetValue("instanceId", out var idEl) == true ? idEl.GetString() : null;
                
                // Support both instanceId and legacy branch/version
                string? instancePath;
                string defaultFileName;
                
                if (!string.IsNullOrEmpty(instanceId))
                {
                    instancePath = instanceService.GetInstancePathById(instanceId);
                    defaultFileName = $"HyPrism-{instanceId.Substring(0, Math.Min(8, instanceId.Length))}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                }
                else
                {
                    var branch = data?["branch"].GetString() ?? "release";
                    var version = data?["version"].GetInt32() ?? 0;
                    instancePath = instanceService.GetInstancePath(branch, version);
                    defaultFileName = $"HyPrism-{branch}-v{version}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                }
                
                if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
                {
                    Reply("hyprism:instance:export:reply", "");
                    return;
                }

                // Show save file dialog
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var savePath = await fileDialog.SaveFileAsync(defaultFileName, "Zip files|*.zip", desktop);
                
                if (string.IsNullOrEmpty(savePath))
                {
                    // User cancelled
                    Reply("hyprism:instance:export:reply", "");
                    return;
                }
                
                // Ensure .zip extension
                if (!savePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    savePath += ".zip";

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
                var fileDialog = _services.GetRequiredService<IFileDialogService>();
                // Show file picker for zip files
                var zipPath = await fileDialog.BrowseZipFileAsync();
                
                if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                {
                    Logger.Info("IPC", "Import cancelled or file not found");
                    Reply("hyprism:instance:import:reply", false);
                    return;
                }
                
                Logger.Info("IPC", $"Importing instance from: {zipPath}");
                
                // Extract to a temp location first to check structure
                var tempDir = Path.Combine(Path.GetTempPath(), $"hyprism-import-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);
                
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                
                // Determine target path - check if zip has meta.json metadata
                var metaPath = Path.Combine(tempDir, "meta.json");
                var branch = "release";
                var version = 0; // Latest
                string? existingId = null;

                if (File.Exists(metaPath))
                {
                    var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(metaPath), JsonOpts);
                    branch = meta?.TryGetValue("branch", out var b) == true ? b.GetString() ?? "release" : "release";
                    if (meta?.TryGetValue("version", out var v) == true) version = v.GetInt32();
                    if (meta?.TryGetValue("id", out var idEl) == true) existingId = idEl.GetString();
                }
                
                // Check if instance with this ID already exists
                var existingInstances = instanceService.GetInstalledInstances();
                var idAlreadyExists = !string.IsNullOrEmpty(existingId) && 
                    existingInstances.Any(i => i.Id == existingId);
                
                // Generate new ID if existing one conflicts or doesn't exist
                var newInstanceId = idAlreadyExists || string.IsNullOrEmpty(existingId) 
                    ? Guid.NewGuid().ToString() 
                    : existingId;
                
                var targetPath = instanceService.CreateInstanceDirectory(branch, newInstanceId);
                
                // Update meta.json with new ID if it was changed
                if (File.Exists(metaPath) && (idAlreadyExists || string.IsNullOrEmpty(existingId)))
                {
                    var metaContent = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(metaPath), JsonOpts);
                    if (metaContent != null)
                    {
                        metaContent["id"] = newInstanceId;
                        File.WriteAllText(metaPath, JsonSerializer.Serialize(metaContent, JsonOpts));
                        Logger.Info("IPC", $"Updated instance ID from '{existingId}' to '{newInstanceId}'");
                    }
                }
                
                // Move contents from temp to target
                foreach (var file in Directory.GetFiles(tempDir))
                {
                    var destFile = Path.Combine(targetPath, Path.GetFileName(file));
                    File.Move(file, destFile, true);
                }
                foreach (var dir in Directory.GetDirectories(tempDir))
                {
                    var destDir = Path.Combine(targetPath, Path.GetFileName(dir));
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                    Directory.Move(dir, destDir);
                }
                
                // Clean up temp directory
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
                
                Logger.Success("IPC", $"Imported instance to: {targetPath}");
                Reply("hyprism:instance:import:reply", true);
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
                var instanceId = data?.TryGetValue("instanceId", out var idElement) == true ? idElement.GetString() : null;
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;

                var instancePath = !string.IsNullOrWhiteSpace(instanceId)
                    ? instanceService.GetInstancePathById(instanceId) ?? instanceService.GetInstancePath(branch, version)
                    : instanceService.GetInstancePath(branch, version);

                if (string.IsNullOrWhiteSpace(instancePath))
                {
                    Reply("hyprism:instance:saves:reply", new List<object>());
                    return;
                }

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
                var instanceId = data?.TryGetValue("instanceId", out var idElement) == true ? idElement.GetString() : null;
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var saveName = data?["saveName"].GetString() ?? "";
                
                var instancePath = !string.IsNullOrWhiteSpace(instanceId)
                    ? instanceService.GetInstancePathById(instanceId) ?? instanceService.GetInstancePath(branch, version)
                    : instanceService.GetInstancePath(branch, version);

                if (string.IsNullOrWhiteSpace(instancePath))
                {
                    return;
                }

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

        // Delete save folder
        Electron.IpcMain.On("hyprism:instance:deleteSave", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?.TryGetValue("instanceId", out var idElement) == true ? idElement.GetString() : null;
                var branch = data?["branch"].GetString() ?? "release";
                var version = data?["version"].GetInt32() ?? 0;
                var saveName = data?["saveName"].GetString() ?? "";

                if (string.IsNullOrWhiteSpace(saveName))
                {
                    Reply("hyprism:instance:deleteSave:reply", false);
                    return;
                }

                var instancePath = !string.IsNullOrWhiteSpace(instanceId)
                    ? instanceService.GetInstancePathById(instanceId) ?? instanceService.GetInstancePath(branch, version)
                    : instanceService.GetInstancePath(branch, version);

                if (string.IsNullOrWhiteSpace(instancePath))
                {
                    Reply("hyprism:instance:deleteSave:reply", false);
                    return;
                }

                var savesPath = Path.GetFullPath(Path.Combine(instancePath, "UserData", "Saves"));
                var targetSavePath = Path.GetFullPath(Path.Combine(savesPath, saveName));

                if (!targetSavePath.StartsWith(savesPath, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning("IPC", $"Blocked save delete outside saves directory: {targetSavePath}");
                    Reply("hyprism:instance:deleteSave:reply", false);
                    return;
                }

                if (!Directory.Exists(targetSavePath))
                {
                    Reply("hyprism:instance:deleteSave:reply", false);
                    return;
                }

                Directory.Delete(targetSavePath, true);
                Logger.Info("IPC", $"Deleted save folder: {targetSavePath}");
                Reply("hyprism:instance:deleteSave:reply", true);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to delete save folder: {ex.Message}");
                Reply("hyprism:instance:deleteSave:reply", false);
            }
        });

        // Get instance icon
        Electron.IpcMain.On("hyprism:instance:getIcon", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?["instanceId"].GetString();
                
                if (string.IsNullOrEmpty(instanceId))
                {
                    Reply("hyprism:instance:getIcon:reply", null);
                    return;
                }
                
                var instancePath = instanceService.GetInstancePathById(instanceId);
                if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
                {
                    Reply("hyprism:instance:getIcon:reply", null);
                    return;
                }
                
                // Check for logo.png first (new format), then icon.png (legacy)
                var logoPath = Path.Combine(instancePath, "logo.png");
                var iconPath = Path.Combine(instancePath, "icon.png");
                
                var foundPath = File.Exists(logoPath) ? logoPath : (File.Exists(iconPath) ? iconPath : null);
                
                if (foundPath != null)
                {
                    var cacheBuster = File.GetLastWriteTimeUtc(foundPath).Ticks;
                    Reply("hyprism:instance:getIcon:reply", $"file://{foundPath.Replace("\\", "/")}?v={cacheBuster}");
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
                var instanceId = data?["instanceId"].GetString();
                var iconBase64 = data?["iconBase64"].GetString();
                
                if (string.IsNullOrEmpty(instanceId))
                {
                    Logger.Warning("IPC", "Set icon failed: no instanceId provided");
                    Reply("hyprism:instance:setIcon:reply", false);
                    return;
                }
                
                var instancePath = instanceService.GetInstancePathById(instanceId);
                if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
                {
                    Logger.Warning("IPC", $"Set icon failed: instance not found by ID: {instanceId}");
                    Reply("hyprism:instance:setIcon:reply", false);
                    return;
                }
                
                var targetIconPath = Path.Combine(instancePath, "logo.png");
                
                if (!string.IsNullOrEmpty(iconBase64))
                {
                    // Remove data URL prefix if present (e.g., "data:image/png;base64,")
                    var base64Data = iconBase64.Contains(",") 
                        ? iconBase64.Substring(iconBase64.IndexOf(",") + 1) 
                        : iconBase64;
                    
                    var imageBytes = Convert.FromBase64String(base64Data);
                    
                    // Resize to 256x256 using ImageSharp
                    using var inputStream = new MemoryStream(imageBytes);
                    using var image = await SixLabors.ImageSharp.Image.LoadAsync(inputStream);
                    
                    image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(256, 256),
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Crop
                    }));
                    
                    await image.SaveAsPngAsync(targetIconPath);
                    
                    Reply("hyprism:instance:setIcon:reply", true);
                    Logger.Info("IPC", $"Set icon for instance {instanceId} (256x256)");
                }
                else
                {
                    // Icon data not provided
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
                var instanceId = data?["instanceId"].GetString();
                var customName = data?["customName"].GetString();
                
                if (string.IsNullOrEmpty(instanceId))
                {
                    Logger.Warning("IPC", "Instance rename failed: no instanceId provided");
                    Reply("hyprism:instance:rename:reply", false);
                    return;
                }
                
                instanceService.SetInstanceCustomNameById(instanceId, customName);
                Reply("hyprism:instance:rename:reply", true);
                Logger.Info("IPC", $"Renamed instance {instanceId} to: {customName ?? "(default)"}");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to rename instance: {ex.Message}");
                Reply("hyprism:instance:rename:reply", false);
            }
        });

        // @ipc invoke hyprism:instance:changeVersion -> boolean
        // Change the version/branch of an existing instance.
        // Clears game client files, updates meta.json, marks non-latest.
        Electron.IpcMain.On("hyprism:instance:changeVersion", (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                var instanceId = data?["instanceId"].GetString();
                var branch = data?["branch"].GetString();
                var version = data?["version"].GetInt32() ?? 0;

                if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(branch))
                {
                    Logger.Warning("IPC", "Instance changeVersion failed: missing instanceId or branch");
                    Reply("hyprism:instance:changeVersion:reply", false);
                    return;
                }

                var result = instanceService.ChangeInstanceVersion(instanceId, branch, version);
                Reply("hyprism:instance:changeVersion:reply", result);
                Logger.Info("IPC", $"Changed instance {instanceId} version to {branch} v{version}: {result}");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to change instance version: {ex.Message}");
                Reply("hyprism:instance:changeVersion:reply", false);
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
        var appPath = _services.GetRequiredService<AppPathConfiguration>();
        var updateService = _services.GetRequiredService<IUpdateService>();

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
                launchAfterDownload = settings.GetLaunchAfterDownload(),
                showDiscordAnnouncements = settings.GetShowDiscordAnnouncements(),
                disableNews = settings.GetDisableNews(),
                backgroundMode = settings.GetBackgroundMode(),
                availableBackgrounds = settings.GetAvailableBackgrounds(),
                accentColor = settings.GetAccentColor(),
                hasCompletedOnboarding = settings.GetHasCompletedOnboarding(),
                onlineMode = settings.GetOnlineMode(),
                authDomain = settings.GetAuthDomain(),
                javaArguments = settings.GetJavaArguments(),
                useCustomJava = settings.GetUseCustomJava(),
                customJavaPath = settings.GetCustomJavaPath(),
                systemMemoryMb = GetSystemMemoryMb(),
                dataDirectory = appPath.AppDir,
                instanceDirectory = settings.GetInstanceDirectory(),
                gpuPreference = settings.GetGpuPreference(),
                gameEnvironmentVariables = settings.GetGameEnvironmentVariables(),
                useDualAuth = settings.GetUseDualAuth(),
                showAlphaMods = settings.GetShowAlphaMods(),
                launcherVersion = UpdateService.GetCurrentVersion()
            });
        });

        Electron.IpcMain.On("hyprism:settings:update", (args) =>
        {
            try
            {
                var oldLauncherBranch = settings.GetLauncherBranch();
                var json = ArgsToJson(args);
                var updates = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (updates != null)
                    foreach (var (key, value) in updates)
                        ApplySetting(settings, key, value);

                // If the user changed the launcher branch, re-check updates immediately.
                // This enables release ↔ beta switching without requiring restart.
                var newLauncherBranch = settings.GetLauncherBranch();
                if (!string.Equals(oldLauncherBranch, newLauncherBranch, StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Run(async () =>
                    {
                        try { await updateService.CheckForLauncherUpdatesAsync(); }
                        catch (Exception ex) { Logger.Warning("Update", $"Update check after channel switch failed: {ex.Message}"); }
                    });
                }

                Reply("hyprism:settings:update:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Settings update failed: {ex.Message}");
                Reply("hyprism:settings:update:reply", new { success = false, error = ex.Message });
            }
        });
        
        // @ipc invoke hyprism:settings:testMirrorSpeed -> MirrorSpeedTestResult
        Electron.IpcMain.On("hyprism:settings:testMirrorSpeed", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var mirrorId = request?.GetValueOrDefault("mirrorId").GetString() ?? "estrogen";
                var forceRefresh = request?.GetValueOrDefault("forceRefresh").ValueKind == JsonValueKind.True;
                
                var versionService = _services.GetRequiredService<IVersionService>();
                var result = await versionService.TestMirrorSpeedAsync(mirrorId, forceRefresh);
                
                Reply("hyprism:settings:testMirrorSpeed:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mirror speed test failed: {ex.Message}");
                Reply("hyprism:settings:testMirrorSpeed:reply", new { 
                    mirrorId = "unknown",
                    mirrorName = "Unknown",
                    mirrorUrl = "",
                    pingMs = 0L,
                    speedMBps = 0.0,
                    isAvailable = false,
                    testedAt = DateTime.UtcNow
                });
            }
        });
        
        // @ipc invoke hyprism:settings:testOfficialSpeed -> MirrorSpeedTestResult
        Electron.IpcMain.On("hyprism:settings:testOfficialSpeed", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var forceRefresh = request?.GetValueOrDefault("forceRefresh").ValueKind == JsonValueKind.True;
                
                var versionService = _services.GetRequiredService<IVersionService>();
                var result = await versionService.TestOfficialSpeedAsync(forceRefresh);
                
                Reply("hyprism:settings:testOfficialSpeed:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Official speed test failed: {ex.Message}");
                Reply("hyprism:settings:testOfficialSpeed:reply", new { 
                    mirrorId = "official",
                    mirrorName = "Hytale",
                    mirrorUrl = "https://cdn.hytale.com",
                    pingMs = -1L,
                    speedMBps = 0.0,
                    isAvailable = false,
                    testedAt = DateTime.UtcNow
                });
            }
        });

        // @ipc invoke hyprism:settings:getMirrors -> MirrorInfo[]
        Electron.IpcMain.On("hyprism:settings:getMirrors", async (_) =>
        {
            await Task.CompletedTask;
            try
            {
                var appPath = _services.GetRequiredService<AppPathConfiguration>();
                var mirrors = MirrorLoaderService.GetAllMirrorMetas(appPath.AppDir);
                var result = mirrors.Select(m => new {
                    id = m.Id,
                    name = m.Name,
                    description = m.Description,
                    priority = m.Priority,
                    enabled = m.Enabled,
                    sourceType = m.SourceType,
                    hostname = GetMirrorHostname(m)
                }).ToList();
                Reply("hyprism:settings:getMirrors:reply", result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Get mirrors failed: {ex.Message}");
                Reply("hyprism:settings:getMirrors:reply", Array.Empty<object>());
            }
        });

        // @ipc invoke hyprism:settings:addMirror -> { success: boolean; error?: string; mirror?: MirrorInfo; }
        Electron.IpcMain.On("hyprism:settings:addMirror", async (args) =>
        {
            try
            {
                var appPath = _services.GetRequiredService<AppPathConfiguration>();
                var json = ArgsToJson(args);
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var url = request?.GetValueOrDefault("url").GetString() ?? "";
                
                if (string.IsNullOrWhiteSpace(url))
                {
                    Reply("hyprism:settings:addMirror:reply", new { success = false, error = "URL is required" });
                    return;
                }
                
                var httpClient = _services.GetRequiredService<HttpClient>();
                var discoveryService = new MirrorDiscoveryService(httpClient);
                var result = await discoveryService.DiscoverMirrorAsync(url);
                
                if (!result.Success || result.Mirror == null)
                {
                    Reply("hyprism:settings:addMirror:reply", new { success = false, error = result.Error ?? "Discovery failed" });
                    return;
                }
                
                // Check if mirror with same ID already exists
                if (MirrorLoaderService.MirrorExists(appPath.AppDir, result.Mirror.Id))
                {
                    // Generate unique ID
                    var baseId = result.Mirror.Id;
                    var counter = 2;
                    while (MirrorLoaderService.MirrorExists(appPath.AppDir, $"{baseId}-{counter}"))
                    {
                        counter++;
                    }
                    result.Mirror.Id = $"{baseId}-{counter}";
                }
                
                MirrorLoaderService.SaveMirror(appPath.AppDir, result.Mirror);
                
                // Reload mirror sources in VersionService
                var versionService = _services.GetRequiredService<IVersionService>();
                versionService.ReloadMirrorSources();
                
                Reply("hyprism:settings:addMirror:reply", new { 
                    success = true, 
                    mirror = new {
                        id = result.Mirror.Id,
                        name = result.Mirror.Name,
                        description = result.Mirror.Description,
                        priority = result.Mirror.Priority,
                        enabled = result.Mirror.Enabled,
                        sourceType = result.Mirror.SourceType,
                        hostname = GetMirrorHostname(result.Mirror),
                        detectedType = result.DetectedType
                    }
                });
                
                Logger.Success("IPC", $"Added mirror: {result.Mirror.Name} ({result.Mirror.Id})");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Add mirror failed: {ex.Message}");
                Reply("hyprism:settings:addMirror:reply", new { success = false, error = ex.Message });
            }
        });

        // @ipc invoke hyprism:settings:deleteMirror -> { success: boolean; }
        Electron.IpcMain.On("hyprism:settings:deleteMirror", async (args) =>
        {
            await Task.CompletedTask;
            try
            {
                var appPath = _services.GetRequiredService<AppPathConfiguration>();
                var json = ArgsToJson(args);
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var mirrorId = request?.GetValueOrDefault("mirrorId").GetString() ?? "";
                
                if (string.IsNullOrWhiteSpace(mirrorId))
                {
                    Reply("hyprism:settings:deleteMirror:reply", new { success = false, error = "Mirror ID is required" });
                    return;
                }
                
                var deleted = MirrorLoaderService.DeleteMirror(appPath.AppDir, mirrorId);
                
                if (deleted)
                {
                    // Reload mirror sources in VersionService
                    var versionService = _services.GetRequiredService<IVersionService>();
                    versionService.ReloadMirrorSources();
                    Logger.Info("IPC", $"Deleted mirror: {mirrorId}");
                }
                
                Reply("hyprism:settings:deleteMirror:reply", new { success = deleted });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Delete mirror failed: {ex.Message}");
                Reply("hyprism:settings:deleteMirror:reply", new { success = false, error = ex.Message });
            }
        });

        // @ipc invoke hyprism:settings:toggleMirror -> { success: boolean; }
        Electron.IpcMain.On("hyprism:settings:toggleMirror", async (args) =>
        {
            await Task.CompletedTask;
            try
            {
                var appPath = _services.GetRequiredService<AppPathConfiguration>();
                var json = ArgsToJson(args);
                var request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var mirrorId = request?.GetValueOrDefault("mirrorId").GetString() ?? "";
                var enabled = request?.GetValueOrDefault("enabled").GetBoolean() ?? true;
                
                var mirrors = MirrorLoaderService.GetAllMirrorMetas(appPath.AppDir);
                var mirror = mirrors.FirstOrDefault(m => m.Id == mirrorId);
                
                if (mirror == null)
                {
                    Reply("hyprism:settings:toggleMirror:reply", new { success = false, error = "Mirror not found" });
                    return;
                }
                
                mirror.Enabled = enabled;
                MirrorLoaderService.SaveMirror(appPath.AppDir, mirror);
                
                // Reload mirror sources in VersionService
                var versionService = _services.GetRequiredService<IVersionService>();
                versionService.ReloadMirrorSources();
                
                Reply("hyprism:settings:toggleMirror:reply", new { success = true });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Toggle mirror failed: {ex.Message}");
                Reply("hyprism:settings:toggleMirror:reply", new { success = false, error = ex.Message });
            }
        });
    }

    private static string GetMirrorHostname(MirrorMeta mirror)
    {
        try
        {
            if (mirror.SourceType == "json-index" && !string.IsNullOrEmpty(mirror.JsonIndex?.ApiUrl))
            {
                return new Uri(mirror.JsonIndex.ApiUrl).Host;
            }
            if (mirror.SourceType == "pattern" && !string.IsNullOrEmpty(mirror.Pattern?.BaseUrl))
            {
                return new Uri(mirror.Pattern.BaseUrl).Host;
            }
        }
        catch { }
        return "";
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
            case "launchAfterDownload": s.SetLaunchAfterDownload(val.GetBoolean()); break;
            case "showDiscordAnnouncements": s.SetShowDiscordAnnouncements(val.GetBoolean()); break;
            case "disableNews": s.SetDisableNews(val.GetBoolean()); break;
            case "backgroundMode": s.SetBackgroundMode(val.GetString() ?? "default"); break;
            case "accentColor": s.SetAccentColor(val.GetString() ?? "#7C5CFC"); break;
            case "onlineMode": s.SetOnlineMode(val.GetBoolean()); break;
            case "authDomain": s.SetAuthDomain(val.GetString() ?? ""); break;
            case "javaArguments": s.SetJavaArguments(val.GetString() ?? ""); break;
            case "useCustomJava": s.SetUseCustomJava(val.GetBoolean()); break;
            case "customJavaPath": s.SetCustomJavaPath(val.GetString() ?? ""); break;
            case "gpuPreference": s.SetGpuPreference(val.GetString() ?? "dedicated"); break;
            case "gameEnvironmentVariables": s.SetGameEnvironmentVariables(val.GetString() ?? ""); break;
            case "useDualAuth": s.SetUseDualAuth(val.GetBoolean()); break;
            case "hasCompletedOnboarding": s.SetHasCompletedOnboarding(val.GetBoolean()); break;
            case "showAlphaMods": s.SetShowAlphaMods(val.GetBoolean()); break;
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
    // @ipc send hyprism:window:restart
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

        Electron.IpcMain.On("hyprism:window:restart", (_) =>
        {
            try
            {
                Electron.App.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to restart app: {ex.Message}");
                GetMainWindow()?.Close();
            }
        });

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
    // @ipc invoke hyprism:mods:search -> ModSearchResult 30000
    // @ipc invoke hyprism:mods:installed -> InstalledMod[]
    // @ipc invoke hyprism:mods:uninstall -> boolean
    // @ipc invoke hyprism:mods:checkUpdates -> InstalledMod[] 30000
    // @ipc invoke hyprism:mods:install -> boolean 300000
    // @ipc invoke hyprism:mods:files -> ModFilesResult
    // @ipc invoke hyprism:mods:info -> ModInfo 30000
    // @ipc invoke hyprism:mods:changelog -> string
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

        string? ResolveModInstancePath(string branch, int version, string? instanceId = null)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                var byId = instanceService.GetInstancePathById(instanceId);
                if (!string.IsNullOrWhiteSpace(byId))
                    return byId;
            }

            var existing = instanceService.FindExistingInstancePath(branch, version);
            if (!string.IsNullOrEmpty(existing))
                return existing;

            var selected = instanceService.GetSelectedInstance();
            if (selected != null)
            {
                var byId = instanceService.GetInstancePathById(selected.Id);
                if (!string.IsNullOrEmpty(byId))
                    return byId;

                var selectedExisting = instanceService.FindExistingInstancePath(selected.Branch, selected.Version);
                if (!string.IsNullOrEmpty(selectedExisting))
                    return selectedExisting;
            }

            return null;
        }

        Electron.IpcMain.On("hyprism:mods:list", (_) =>
        {
            try
            {
                var branch = config.Configuration.LauncherBranch ?? "release";
                var instancePath = ResolveModInstancePath(branch, 0);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Reply("hyprism:mods:list:reply", new List<object>());
                    return;
                }

                Reply("hyprism:mods:list:reply", modService.GetInstanceInstalledMods(instancePath));
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
                var root = doc.RootElement;
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;
                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", $"Mods installed skipped: no target instance found for {branch}/{version}");
                    Reply("hyprism:mods:installed:reply", new List<object>());
                    return;
                }
                
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
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;
                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", $"Mods uninstall skipped: no target instance found for {branch}/{version}");
                    Reply("hyprism:mods:uninstall:reply", false);
                    return;
                }
                
                // Get current mods, remove the one with matching ID, save back
                var mods = modService.GetInstanceInstalledMods(instancePath);
                var modToRemove = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
                if (modToRemove != null)
                {
                    mods.Remove(modToRemove);
                    
                    // Delete the actual mod file if it exists
                    if (!string.IsNullOrEmpty(modToRemove.FileName))
                    {
                        var modFilePath = Path.Combine(instancePath, "UserData", "Mods", modToRemove.FileName);
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
                var root = doc.RootElement;
                var branch = root.GetProperty("branch").GetString() ?? "release";
                var version = root.GetProperty("version").GetInt32();
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;
                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", $"Mods check updates skipped: no target instance found for {branch}/{version}");
                    Reply("hyprism:mods:checkUpdates:reply", new List<object>());
                    return;
                }
                
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
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;

                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", "Mods install failed: no target instance selected");
                    Reply("hyprism:mods:install:reply", new { success = false, error = "No target instance selected" });
                    return;
                }
                
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

        // Get single mod metadata (by numeric id or slug)
        Electron.IpcMain.On("hyprism:mods:info", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";

                var mod = await modService.GetModAsync(modId);
                Reply("hyprism:mods:info:reply", mod ?? new ModInfo());
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods info failed: {ex.Message}");
                Reply("hyprism:mods:info:reply", new ModInfo());
            }
        });

        // Get changelog for a mod file
        Electron.IpcMain.On("hyprism:mods:changelog", async (args) =>
        {
            try
            {
                var json = ArgsToJson(args);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var modId = root.GetProperty("modId").GetString() ?? "";
                var fileId = root.GetProperty("fileId").GetString() ?? "";

                var text = await modService.GetModFileChangelogAsync(modId, fileId);
                Reply("hyprism:mods:changelog:reply", text ?? "");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Mods changelog failed: {ex.Message}");
                Reply("hyprism:mods:changelog:reply", "");
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
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;

                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", "Mods install local failed: no target instance selected");
                    Reply("hyprism:mods:installLocal:reply", false);
                    return;
                }
                
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
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;

                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", "Mods install base64 failed: no target instance selected");
                    Reply("hyprism:mods:installBase64:reply", false);
                    return;
                }
                
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
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;

                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", "Open mods folder skipped: no target instance selected");
                    return;
                }
                
                var modsPath = Path.Combine(instancePath, "UserData", "Mods");
                ModService.EnsureModsDirectory(modsPath);
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
                var instanceId = root.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;
                var instancePath = ResolveModInstancePath(branch, version, instanceId);
                if (string.IsNullOrEmpty(instancePath))
                {
                    Logger.Warning("IPC", $"Mods toggle skipped: no target instance found for {branch}/{version}");
                    Reply("hyprism:mods:toggle:reply", false);
                    return;
                }
                
                var mods = modService.GetInstanceInstalledMods(instancePath);
                var mod = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
                if (mod == null || string.IsNullOrEmpty(mod.FileName))
                {
                    Reply("hyprism:mods:toggle:reply", false);
                    return;
                }
                
                var modsDir = Path.Combine(instancePath, "UserData", "Mods");
                var currentPath = Path.Combine(modsDir, mod.FileName);
                var fileName = mod.FileName;
                var sourceExists = File.Exists(currentPath);

                if (!sourceExists)
                {
                    // Recover from stale manifest filenames by probing likely variants
                    var stem = Path.GetFileNameWithoutExtension(fileName);
                    var candidates = new[]
                    {
                        Path.Combine(modsDir, fileName),
                        Path.Combine(modsDir, $"{stem}.jar"),
                        Path.Combine(modsDir, $"{stem}.zip"),
                        Path.Combine(modsDir, $"{stem}.disabled"),
                        Path.Combine(modsDir, $"{stem}.jar.disabled"),
                        Path.Combine(modsDir, $"{stem}.zip.disabled"),
                    };

                    var found = candidates.FirstOrDefault(File.Exists);
                    if (!string.IsNullOrEmpty(found))
                    {
                        currentPath = found;
                        fileName = Path.GetFileName(found);
                        mod.FileName = fileName;
                        sourceExists = true;
                    }
                }

                if (!sourceExists)
                {
                    Reply("hyprism:mods:toggle:reply", false);
                    return;
                }
                
                if (mod.Enabled)
                {
                    // Disable: rename file.jar/file.zip -> file.disabled
                    var currentFileName = Path.GetFileName(currentPath);
                    var baseName = Path.GetFileNameWithoutExtension(currentFileName);
                    var ext = Path.GetExtension(currentFileName).ToLowerInvariant();

                    if (currentFileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        mod.Enabled = false;
                        mod.FileName = currentFileName;
                    }
                    else
                    {
                        if (ext is ".jar" or ".zip")
                        {
                            mod.DisabledOriginalExtension = ext;
                        }

                        var disabledFileName = $"{baseName}.disabled";
                        var disabledPath = Path.Combine(modsDir, disabledFileName);
                        File.Move(currentPath, disabledPath, true);
                        mod.FileName = disabledFileName;
                        mod.Enabled = false;
                        Logger.Info("IPC", $"Disabled mod: {mod.Name}");
                    }
                }
                else
                {
                    // Enable: rename *.disabled -> *.jar or *.zip (restored)
                    var currentFileName = Path.GetFileName(currentPath);
                    var stem = currentFileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? currentFileName[..^".disabled".Length]
                        : Path.GetFileNameWithoutExtension(currentFileName);

                    string restoreExtension;
                    if (!string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                    {
                        restoreExtension = mod.DisabledOriginalExtension.StartsWith('.')
                            ? mod.DisabledOriginalExtension
                            : $".{mod.DisabledOriginalExtension}";
                    }
                    else if (stem.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        restoreExtension = "";
                    }
                    else
                    {
                        restoreExtension = ".jar";
                    }

                    var enabledFileName = string.IsNullOrEmpty(restoreExtension)
                        ? stem
                        : $"{stem}{restoreExtension}";
                    var enabledPath = Path.Combine(modsDir, enabledFileName);

                    File.Move(currentPath, enabledPath, true);
                    mod.FileName = enabledFileName;
                    mod.Enabled = true;
                    mod.DisabledOriginalExtension = "";
                    Logger.Info("IPC", $"Enabled mod: {mod.Name}");
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
    // @ipc invoke hyprism:system:platform -> { os: string; isLinux: boolean; isWindows: boolean; isMacOS: boolean }

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

        Electron.IpcMain.On("hyprism:system:platform", (_) =>
        {
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var os = isLinux ? "linux" : isWindows ? "windows" : isMacOS ? "macos" : "unknown";
            
            Reply("hyprism:system:platform:reply", new { os, isLinux, isWindows, isMacOS });
        });
    }

    // #endregion

    // #region Console (Electron renderer → .NET Logger)
    // @ipc send hyprism:console:log
    // @ipc send hyprism:console:warn
    // @ipc send hyprism:console:error
    // @ipc invoke hyprism:logs:get -> string[]

    private void RegisterConsoleHandlers()
    {
        Electron.IpcMain.On("hyprism:console:log", (args) =>
            Logger.Info("Renderer", ArgsToString(args)));

        Electron.IpcMain.On("hyprism:console:warn", (args) =>
            Logger.Warning("Renderer", ArgsToString(args)));

        Electron.IpcMain.On("hyprism:console:error", (args) =>
            Logger.Error("Renderer", ArgsToString(args)));

        // Get recent logs from in-memory buffer
        Electron.IpcMain.On("hyprism:logs:get", (args) =>
        {
            try
            {
                int count = 100; // Default to max buffer size
                if (args != null)
                {
                    try
                    {
                        var json = ArgsToJson(args);
                        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
                        if (data != null && data.TryGetValue("count", out var countEl))
                        {
                            count = countEl.GetInt32();
                        }
                    }
                    catch { /* use default */ }
                }
                var logs = Logger.GetRecentLogs(count);
                Reply("hyprism:logs:get:reply", logs);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to get logs: {ex.Message}");
                Reply("hyprism:logs:get:reply", new List<string>());
            }
        });
    }

    // #endregion

    // #region File Dialog
    // @ipc invoke hyprism:file:browseFolder -> string | null 300000
    // @ipc invoke hyprism:file:browseJavaExecutable -> string | null 300000
    // @ipc invoke hyprism:file:browseModFiles -> string[]
    // @ipc invoke hyprism:file:exists -> boolean
    // @ipc invoke hyprism:mods:exportToFolder -> string
    // @ipc invoke hyprism:mods:importList -> number
    // @ipc invoke hyprism:settings:launcherPath -> string
    // @ipc invoke hyprism:settings:defaultInstanceDir -> string
    // @ipc invoke hyprism:settings:setInstanceDir -> { success: boolean, path: string, noop?: boolean, reason?: string, error?: string } 300000

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

        // Browse Java executable
        Electron.IpcMain.On("hyprism:file:browseJavaExecutable", async (_) =>
        {
            try
            {
                var selected = await fileDialog.BrowseJavaExecutableAsync();
                Reply("hyprism:file:browseJavaExecutable:reply", selected ?? "");
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to browse Java executable: {ex.Message}");
                Reply("hyprism:file:browseJavaExecutable:reply", "");
            }
        });

        // Check file existence
        Electron.IpcMain.On("hyprism:file:exists", (args) =>
        {
            try
            {
                var path = ArgsToString(args)?.Trim() ?? string.Empty;
                var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                Reply("hyprism:file:exists:reply", exists);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to check file existence: {ex.Message}");
                Reply("hyprism:file:exists:reply", false);
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
                    var modsDir = Path.Combine(instancePath, "UserData", "Mods");
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
            var defaultDir = Path.Combine(appPath.AppDir, "Instances");
            Reply("hyprism:settings:defaultInstanceDir:reply", defaultDir);
        });
        
        // Set instance directory - moves all instances to new location
        Electron.IpcMain.On("hyprism:settings:setInstanceDir", async (args) =>
        {
            try
            {
                var progressService = _services.GetRequiredService<ProgressNotificationService>();
                var path = ArgsToString(args);
                Logger.Info("IPC", $"Setting instance directory to: {path}");
                var resetToDefault = string.IsNullOrWhiteSpace(path);
                
                // Expand and validate path - escape special characters
                var newPath = resetToDefault
                    ? Path.Combine(appPath.AppDir, "Instances")
                    : Environment.ExpandEnvironmentVariables(path.Trim());

                if (!Path.IsPathRooted(newPath))
                {
                    newPath = Path.GetFullPath(Path.Combine(appPath.AppDir, newPath));
                }
                else
                {
                    newPath = Path.GetFullPath(newPath);
                }
                
                // Get current instance root
                var currentRoot = Path.GetFullPath(instanceService.GetInstanceRoot());
                
                // If same directory, just update config
                if (Path.GetFullPath(currentRoot).Equals(Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("IPC", "New instance directory is same as current, skipping move");
                    Reply("hyprism:settings:setInstanceDir:reply", new { success = true, path = newPath, noop = true, reason = "already-current-path" });
                    return;
                }

                // Prevent recursive move into current directory subtree
                if (newPath.StartsWith(currentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    Reply("hyprism:settings:setInstanceDir:reply", new { success = false, path = "", error = "Target directory cannot be inside current instance directory" });
                    return;
                }
                
                // Create target directory
                Directory.CreateDirectory(newPath);
                
                // Get all files to move
                var filesToMove = new List<(string source, string dest)>();
                if (Directory.Exists(currentRoot))
                {
                    await Task.Run(() => CollectFilesRecursive(currentRoot, newPath, currentRoot, filesToMove));
                }

                // Move larger files first to avoid late progress jumps and stale filename display
                filesToMove = filesToMove
                    .OrderByDescending(f =>
                    {
                        try { return new FileInfo(f.source).Length; }
                        catch { return 0; }
                    })
                    .ToList();
                
                if (filesToMove.Count == 0)
                {
                    // No files to move, just update config
                    if (resetToDefault)
                    {
                        var cfg = config.Configuration;
                        cfg.InstanceDirectory = string.Empty;
                        config.SaveConfig();
                        Reply("hyprism:settings:setInstanceDir:reply", new { success = true, path = newPath });
                    }
                    else
                    {
                        var result = await config.SetInstanceDirectoryAsync(newPath);
                        Reply("hyprism:settings:setInstanceDir:reply", new { success = result != null, path = result ?? newPath });
                    }
                    return;
                }
                
                // Move files with progress
                long totalSize = 0;
                long movedSize = 0;
                foreach (var (source, _) in filesToMove)
                {
                    try { totalSize += new FileInfo(source).Length; } catch { /* ignore */ }
                }
                
                progressService.SendProgress("moving-instances", 0, "settings.dataSettings.movingData", null, 0, totalSize);
                
                var movedCount = 0;
                foreach (var (source, dest) in filesToMove)
                {
                    try
                    {
                        var destDir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        var preProgress = totalSize > 0
                            ? (int)Math.Clamp((movedSize * 100) / totalSize, 0, 99)
                            : (movedCount * 100 / filesToMove.Count);
                        progressService.SendProgress("moving-instances", preProgress, "settings.dataSettings.movingDataHint", new object[] { Path.GetFileName(source) }, movedSize, totalSize);
                        
                        // Copy file (safer than move across volumes)
                        File.Copy(source, dest, true);
                        
                        var fileSize = new FileInfo(dest).Length;
                        movedSize += fileSize;
                        movedCount++;
                        
                        var progress = totalSize > 0 ? (int)((movedSize * 100) / totalSize) : (movedCount * 100 / filesToMove.Count);
                        progressService.SendProgress("moving-instances", progress, "settings.dataSettings.movingDataHint", new object[] { Path.GetFileName(source) }, movedSize, totalSize);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("IPC", $"Failed to copy file {source}: {ex.Message}");
                    }
                }
                
                // Update config first
                bool setSuccess;
                if (resetToDefault)
                {
                    var cfg = config.Configuration;
                    cfg.InstanceDirectory = string.Empty;
                    config.SaveConfig();
                    setSuccess = true;
                }
                else
                {
                    var setResultPath = await config.SetInstanceDirectoryAsync(newPath);
                    setSuccess = setResultPath != null;
                }
                
                // Delete old files only if config update succeeded
                if (setSuccess)
                {
                    try
                    {
                        // Delete old directory contents (not the directory itself if it's app dir)
                        if (!currentRoot.Equals(Path.Combine(appPath.AppDir, "Instances"), StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Delete(currentRoot, true);
                        }
                        else
                        {
                            // Just delete contents for default location
                            foreach (var dir in Directory.GetDirectories(currentRoot))
                                Directory.Delete(dir, true);
                            foreach (var file in Directory.GetFiles(currentRoot))
                                File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("IPC", $"Failed to clean up old instance directory: {ex.Message}");
                    }
                }
                
                progressService.SendProgress("moving-instances-complete", 100, "settings.dataSettings.moveComplete", null, totalSize, totalSize);
                Logger.Success("IPC", $"Instance directory moved to: {newPath}");
                Reply("hyprism:settings:setInstanceDir:reply", new { success = setSuccess, path = newPath });
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"Failed to set instance directory: {ex.Message}");
                Reply("hyprism:settings:setInstanceDir:reply", new { success = false, path = "", error = ex.Message });
            }
        });
        
    }

    /// <summary>
    /// Recursively collects all files from source directory for moving to destination.
    /// </summary>
    private static void CollectFilesRecursive(string sourceDir, string destRoot, string originalRoot, List<(string source, string dest)> files)
    {
        try
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var relativePath = Path.GetRelativePath(originalRoot, file);
                var destPath = Path.Combine(destRoot, relativePath);
                files.Add((file, destPath));
            }
            
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CollectFilesRecursive(dir, destRoot, originalRoot, files);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("IPC", $"Failed to enumerate directory {sourceDir}: {ex.Message}");
        }
    }

    // #endregion
}
