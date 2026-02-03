using System.Diagnostics;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core;
using HyPrism.Services.User;

namespace HyPrism.Services.Game;

public class GameSessionService
{
    private readonly ConfigService _configService;
    private readonly InstanceService _instanceService;
    private readonly VersionService _versionService;
    private readonly UpdateService _updateService;
    private readonly LaunchService _launchService;
    private readonly ButlerService _butlerService;
    private readonly DownloadService _downloadService;
    private readonly ModService _modService;
    private readonly SkinService _skinService;
    private readonly UserIdentityService _userIdentityService;
    private readonly GameProcessService _gameProcessService;
    private readonly ProgressNotificationService _progressService;
    private readonly DiscordService _discordService;
    private readonly HttpClient _httpClient;
    private readonly string _appDir;

    private CancellationTokenSource? _downloadCts;

    public GameSessionService(
        ConfigService configService,
        InstanceService instanceService,
        VersionService versionService,
        UpdateService updateService,
        LaunchService launchService,
        ButlerService butlerService,
        DownloadService downloadService,
        ModService modService,
        SkinService skinService,
        UserIdentityService userIdentityService,
        GameProcessService gameProcessService,
        ProgressNotificationService progressService,
        DiscordService discordService,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _configService = configService;
        _instanceService = instanceService;
        _versionService = versionService;
        _updateService = updateService;
        _launchService = launchService;
        _butlerService = butlerService;
        _downloadService = downloadService;
        _modService = modService;
        _skinService = skinService;
        _userIdentityService = userIdentityService;
        _gameProcessService = gameProcessService;
        _progressService = progressService;
        _discordService = discordService;
        _httpClient = httpClient;
        _appDir = appPath.AppDir;
    }

    private Config _config => _configService.Configuration;

    public async Task<DownloadProgress> DownloadAndLaunchAsync()
    {
        try
        {
            _downloadCts = new CancellationTokenSource();
            
            string branch = UtilityService.NormalizeVersionType(_config.VersionType);
            var versions = await _versionService.GetVersionListAsync(branch);
            if (versions.Count == 0)
            {
                return new DownloadProgress { Error = "No versions available for this branch" };
            }

            bool isLatestInstance = _config.SelectedVersion == 0;
            int targetVersion = _config.SelectedVersion > 0 ? _config.SelectedVersion : versions[0];
            if (!versions.Contains(targetVersion))
            {
                targetVersion = versions[0];
            }

            // Resolve Instance Path
            string versionPath = _instanceService.ResolveInstancePath(branch, isLatestInstance ? 0 : targetVersion, preferExisting: true);
            Directory.CreateDirectory(versionPath);

            // Check if we need to download/install - verify all components
            // The game is installed if the Client executable exists - that's all we need to check
            bool gameIsInstalled = _instanceService.IsClientPresent(versionPath);
            
            Logger.Info("Download", $"=== INSTALL CHECK ===");
            Logger.Info("Download", $"Version path: {versionPath}");
            Logger.Info("Download", $"Is latest instance: {isLatestInstance}");
            Logger.Info("Download", $"Target version: {targetVersion}");
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}");
            
            // If game is already installed, check for updates then launch
            if (gameIsInstalled)
            {
                Logger.Success("Download", "Game is already installed");
                
                // Check if we need a differential update (only for latest instance)
                if (isLatestInstance)
                {
                    var info = _instanceService.LoadLatestInfo(branch);
                    int installedVersion = info?.Version ?? 0;
                    int latestVersion = versions[0];
                    
                    // If no latest.json exists, we need to determine the installed version
                    if (installedVersion == 0)
                    {
                        // Check logic for Butler receipt...
                        var receiptPath = Path.Combine(versionPath, ".itch", "receipt.json.gz");
                        bool hasButlerReceipt = File.Exists(receiptPath);
                        
                        if (hasButlerReceipt)
                        {
                            var cacheDir = Path.Combine(_appDir, "cache");
                            if (Directory.Exists(cacheDir))
                            {
                                var pwrFiles = Directory.GetFiles(cacheDir, $"{branch}_patch_*.pwr")
                                    .Concat(Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
                                    .Select(f => Path.GetFileNameWithoutExtension(f))
                                    .SelectMany(n => {
                                        var parts = n.Split('_');
                                        var vs = new List<int>();
                                        foreach (var part in parts)
                                        {
                                            if (int.TryParse(part, out var v) && v > 0)
                                            {
                                                vs.Add(v);
                                            }
                                        }
                                        return vs;
                                    })
                                    .OrderByDescending(v => v)
                                    .ToList();
                                
                                if (pwrFiles.Count > 0)
                                {
                                    installedVersion = pwrFiles[0];
                                    Logger.Info("Download", $"Detected installed version from cache: v{installedVersion}");
                                    _instanceService.SaveLatestInfo(branch, installedVersion);
                                }
                            }
                            
                            if (installedVersion == 0)
                            {
                                Logger.Info("Download", $"Butler receipt exists but no version info, launching as-is (user can UPDATE manually)");
                            }
                        }
                        else
                        {
                            Logger.Info("Download", $"No Butler receipt, launching current installation as-is (user can UPDATE manually)");
                        }
                    }
                    
                    Logger.Info("Download", $"Installed version: {installedVersion}, Latest version: {latestVersion}");
                    
                    // Only apply differential update if we're BEHIND the latest version
                    if (installedVersion > 0 && installedVersion < latestVersion)
                    {
                        Logger.Info("Download", $"Differential update available: {installedVersion} -> {latestVersion}");
                        _progressService.ReportDownloadProgress("update", 0, $"Updating game from v{installedVersion} to v{latestVersion}...", 0, 0);
                        
                        try
                        {
                            var patchesToApply = _versionService.GetPatchSequence(installedVersion, latestVersion);
                            Logger.Info("Download", $"Patches to apply: {string.Join(" -> ", patchesToApply)}");
                            
                            for (int i = 0; i < patchesToApply.Count; i++)
                            {
                                int patchVersion = patchesToApply[i];
                                ThrowIfCancelled();
                                
                                int baseProgress = (i * 90) / patchesToApply.Count;
                                int progressPerPatch = 90 / patchesToApply.Count;
                                
                                _progressService.ReportDownloadProgress("update", baseProgress, $"Downloading patch {i + 1}/{patchesToApply.Count} (v{patchVersion})...", 0, 0);
                                
                                // Ensure Butler is installed
                                await _butlerService.EnsureButlerInstalledAsync((p, m) => { });
                                
                                // Download the PWR patch
                                var patchOs = UtilityService.GetOS();
                                var patchArch = UtilityService.GetArch();
                                var patchBranchType = UtilityService.NormalizeVersionType(branch);
                                string patchUrl = $"https://game-patches.hytale.com/patches/{patchOs}/{patchArch}/{patchBranchType}/0/{patchVersion}.pwr";
                                string patchPwrPath = Path.Combine(_appDir, "cache", $"{branch}_patch_{patchVersion}.pwr");
                                
                                Directory.CreateDirectory(Path.GetDirectoryName(patchPwrPath)!);
                                Logger.Info("Download", $"Downloading patch: {patchUrl}");
                                
                                try
                                {
                                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, patchUrl);
                                    using var headResponse = await _httpClient.SendAsync(headRequest);
                                    
                                    if (!headResponse.IsSuccessStatusCode)
                                    {
                                        Logger.Warning("Download", $"Patch file not found at {patchUrl}, skipping differential update");
                                        throw new Exception("Patch file not available");
                                    }
                                    
                                    var contentLength = headResponse.Content.Headers.ContentLength ?? 0;
                                    if (contentLength > 500 * 1024 * 1024)
                                    {
                                        Logger.Warning("Download", $"Patch file is too large ({contentLength / 1024 / 1024} MB), likely wrong version detection");
                                        throw new Exception("Patch file unexpectedly large - version detection may be incorrect");
                                    }
                                }
                                catch (HttpRequestException)
                                {
                                    Logger.Warning("Download", $"Cannot check patch file at {patchUrl}, skipping differential update");
                                    throw new Exception("Cannot access patch file");
                                }
                                
                                await _downloadService.DownloadFileAsync(patchUrl, patchPwrPath, (progress, downloaded, total) =>
                                {
                                    int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    _progressService.ReportDownloadProgress("update", mappedProgress, $"Downloading patch {i + 1}/{patchesToApply.Count}... {progress}%", downloaded, total);
                                }, _downloadCts.Token);
                                
                                ThrowIfCancelled();
                                
                                int applyBaseProgress = baseProgress + (progressPerPatch / 2);
                                _progressService.ReportDownloadProgress("update", applyBaseProgress, $"Applying patch {i + 1}/{patchesToApply.Count}...", 0, 0);
                                
                                await _butlerService.ApplyPwrAsync(patchPwrPath, versionPath, (progress, message) =>
                                {
                                    int mappedProgress = applyBaseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    _progressService.ReportDownloadProgress("update", mappedProgress, message, 0, 0);
                                }, _downloadCts.Token);
                                
                                if (File.Exists(patchPwrPath))
                                {
                                    try { File.Delete(patchPwrPath); } catch { }
                                }
                                
                                _instanceService.SaveLatestInfo(branch, patchVersion);
                                Logger.Success("Download", $"Patch {patchVersion} applied successfully");
                            }
                            
                            Logger.Success("Download", $"Differential update complete: now at v{latestVersion}");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Download", $"Differential update failed: {ex.Message}");
                            Logger.Warning("Download", "Keeping current version, user can try UPDATE again later");
                        }
                    }
                    else if (installedVersion >= latestVersion)
                    {
                        Logger.Info("Download", "Already at latest version, no update needed");
                        _instanceService.SaveLatestInfo(branch, latestVersion);
                    }
                }
                
                // VC++ Redist check
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _progressService.ReportDownloadProgress("install", 94, "Checking Visual C++ Runtime...", 0, 0);
                    try
                    {
                        await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 94 + (int)(progress * 0.02);
                            _progressService.ReportDownloadProgress("install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                    }
                }
                
                string jrePath = _launchService.GetJavaPath();
                if (!File.Exists(jrePath))
                {
                    Logger.Info("Download", "JRE missing, installing...");
                    _progressService.ReportDownloadProgress("install", 96, "Installing Java Runtime...", 0, 0);
                    try
                    {
                        await _launchService.EnsureJREInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 96 + (int)(progress * 0.03);
                            _progressService.ReportDownloadProgress("install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                        return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
                    }
                }
                
                _progressService.ReportDownloadProgress("complete", 100, "Launching game...", 0, 0);
                try
                {
                    await LaunchGameAsync(versionPath, branch);
                    return new DownloadProgress { Success = true, Progress = 100 };
                }
                catch (Exception ex)
                {
                    Logger.Error("Game", $"Launch failed: {ex.Message}");
                    _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
                    return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
                }
            }
            
            // Game is NOT installed
            Logger.Info("Download", "Game not installed, starting download...");

            _progressService.ReportDownloadProgress("download", 0, "Preparing download...", 0, 0);
            
            try
            {
                await _butlerService.EnsureButlerInstalledAsync((progress, message) =>
                {
                    int mappedProgress = (int)(progress * 0.05);
                    _progressService.ReportDownloadProgress("download", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Butler install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
            }

            ThrowIfCancelled();
            
            string osName = UtilityService.GetOS();
            string arch = UtilityService.GetArch();
            string apiVersionType = UtilityService.NormalizeVersionType(branch);
            string downloadUrl = $"https://game-patches.hytale.com/patches/{osName}/{arch}/{apiVersionType}/0/{targetVersion}.pwr";
            string pwrPath = Path.Combine(_appDir, "cache", $"{branch}_{(isLatestInstance ? "latest" : "version")}_{targetVersion}.pwr");
            
            Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);
            
            Logger.Info("Download", $"Downloading: {downloadUrl}");
            
            await _downloadService.DownloadFileAsync(downloadUrl, pwrPath, (progress, downloaded, total) =>
            {
                int mappedProgress = 5 + (int)(progress * 0.60);
                _progressService.ReportDownloadProgress("download", mappedProgress, $"Downloading... {progress}%", downloaded, total);
            }, _downloadCts.Token);
            
            // Extract PWR
            _progressService.ReportDownloadProgress("install", 65, "Installing game with Butler...", 0, 0);
            
            try
            {
                await _butlerService.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
                {
                    int mappedProgress = 65 + (int)(progress * 0.20);
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, 0, 0);
                }, _downloadCts.Token);
                
                if (File.Exists(pwrPath))
                {
                    try { File.Delete(pwrPath); } catch { }
                }
                
                ThrowIfCancelled();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
            }

            if (isLatestInstance)
            {
                _instanceService.SaveLatestInfo(branch, targetVersion);
            }
            
            _progressService.ReportDownloadProgress("complete", 95, "Download complete!", 0, 0);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _progressService.ReportDownloadProgress("install", 95, "Checking Visual C++ Runtime...", 0, 0);
                try
                {
                    await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                    {
                        int mappedProgress = 95 + (int)(progress * 0.01);
                        _progressService.ReportDownloadProgress("install", mappedProgress, message, 0, 0);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                }
            }

            _progressService.ReportDownloadProgress("install", 96, "Checking Java Runtime...", 0, 0);
            try
            {
                await _launchService.EnsureJREInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 96 + (int)(progress * 0.03); 
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
            }

            ThrowIfCancelled();

            _progressService.ReportDownloadProgress("complete", 100, "Launching game...", 0, 0);

            try
            {
                await LaunchGameAsync(versionPath, branch);
                return new DownloadProgress { Success = true, Progress = 100 };
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Launch failed: {ex.Message}");
                _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
                return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Operation cancelled");
            return new DownloadProgress { Error = "Cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Fatal error: {ex.Message}");
            Logger.Error("Download", ex.ToString());
            _progressService.ReportError("fatal", "Fatal error", ex.ToString());
            return new DownloadProgress { Error = $"Fatal error: {ex.Message}" };
        }
        finally 
        {
            _downloadCts = null;
        }
    }

    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private void ThrowIfCancelled()
    {
        _downloadCts?.Token.ThrowIfCancellationRequested();
    }

    private async Task LaunchGameAsync(string versionPath, string branch)
    {
        Logger.Info("Game", $"Preparing to launch from {versionPath}");
        
        string executable;
        string workingDir;
        string gameDir = versionPath;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            executable = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            workingDir = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = Path.Combine(versionPath, "Client", "HytaleClient.exe");
            workingDir = Path.Combine(versionPath, "Client");
        }
        else
        {
            executable = Path.Combine(versionPath, "Client", "HytaleClient");
            workingDir = Path.Combine(versionPath, "Client");
        }
        
        if (!File.Exists(executable))
        {
            Logger.Error("Game", $"Game client not found at {executable}");
            throw new Exception($"Game client not found at {executable}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
            UtilityService.ClearMacQuarantine(appBundle);
            Logger.Info("Game", "Cleared macOS quarantine attributes before patching");
        }

        bool enablePatching = true;
        if (enablePatching && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            try
            {
                string baseDomain = _config.AuthDomain;
                if (baseDomain.StartsWith("sessions."))
                {
                    baseDomain = baseDomain.Substring("sessions.".Length);
                }
                
                Logger.Info("Game", $"Patching binary: hytale.com -> {baseDomain}");
                var patcher = new ClientPatcher(baseDomain);
                
                var patchResult = patcher.EnsureClientPatched(versionPath, (msg, progress) =>
                {
                   Logger.Info("Patcher", progress.HasValue ? $"{msg} ({progress}%)" : msg);
                });
                
                Logger.Info("Game", $"Patching server JAR: sessions.hytale.com -> sessions.{baseDomain}");
                var serverPatchResult = patcher.PatchServerJar(versionPath, (msg, progress) =>
                {
                   Logger.Info("Patcher", progress.HasValue ? $"{msg} ({progress}%)" : msg);
                });
                
                if (patchResult.Success)
                {
                    if (patchResult.PatchCount > 0 && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        try
                        {
                            Logger.Info("Game", "Re-signing patched binary...");
                            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
                            bool signed = ClientPatcher.SignMacOSBinary(appBundle);
                            if (signed) Logger.Success("Game", "Binary re-signed successfully");
                            else Logger.Warning("Game", "Binary signing failed - game may not launch");
                        }
                        catch (Exception signEx)
                        {
                            Logger.Warning("Game", $"Error re-signing binary: {signEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Game", $"Error during binary patching: {ex.Message}");
            }
        }

        string sessionUuid = _userIdentityService.GetUuidForUser(_config.Nick);
        Logger.Info("Game", $"Using UUID for user '{_config.Nick}': {sessionUuid}");

        string? identityToken = null;
        string? sessionToken = null;
        
        if (_config.OnlineMode && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            Logger.Info("Game", $"Online mode enabled - fetching auth tokens from {_config.AuthDomain}...");
            try
            {
                var authService = new AuthService(_httpClient, _config.AuthDomain);
                var tokenResult = await authService.GetGameSessionTokenAsync(sessionUuid, _config.Nick);
                
                if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Token))
                {
                    identityToken = tokenResult.Token;
                    sessionToken = tokenResult.SessionToken ?? tokenResult.Token;
                    Logger.Success("Game", "Identity token obtained successfully");
                }
                else
                {
                    Logger.Warning("Game", $"Could not get auth token: {tokenResult.Error}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Error fetching auth token: {ex.Message}");
            }
        }

        string javaPath = _launchService.GetJavaPath();
        if (!File.Exists(javaPath)) throw new Exception($"Java not found at {javaPath}");
        
        string userDataDir = _instanceService.GetInstanceUserDataPath(versionPath);
        Directory.CreateDirectory(userDataDir);
        
        var currentProfile = _config.Profiles?.FirstOrDefault(p => p.UUID == sessionUuid);
        if (currentProfile != null)
        {
            _skinService.RestoreProfileSkinData(currentProfile);
            Logger.Info("Game", $"Restored skin data for profile '{currentProfile.Name}'");
            
            string skinCachePath = Path.Combine(userDataDir, "CachedPlayerSkins", $"{currentProfile.UUID}.json");
            if (File.Exists(skinCachePath))
            {
                _skinService.StartSkinProtection(currentProfile, skinCachePath);
            }
        }

        Logger.Info("Game", $"Launching: {executable}");
        Logger.Info("Game", $"Java: {javaPath}");
        Logger.Info("Game", $"AppDir: {gameDir}");
        Logger.Info("Game", $"UserData: {userDataDir}");
        Logger.Info("Game", $"Online Mode: {_config.OnlineMode}");
        Logger.Info("Game", $"Session UUID: {sessionUuid}");

        ProcessStartInfo startInfo;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Use ArgumentList for proper escaping
            startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            
            // Add arguments using ArgumentList
            startInfo.ArgumentList.Add("--app-dir");
            startInfo.ArgumentList.Add(gameDir);
            startInfo.ArgumentList.Add("--user-dir");
            startInfo.ArgumentList.Add(userDataDir);
            startInfo.ArgumentList.Add("--java-exec");
            startInfo.ArgumentList.Add(javaPath);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(_config.Nick);
            
            if (_config.OnlineMode && !string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
            {
                startInfo.ArgumentList.Add("--auth-mode");
                startInfo.ArgumentList.Add("authenticated");
                startInfo.ArgumentList.Add("--uuid");
                startInfo.ArgumentList.Add(sessionUuid);
                startInfo.ArgumentList.Add("--identity-token");
                startInfo.ArgumentList.Add(identityToken);
                startInfo.ArgumentList.Add("--session-token");
                startInfo.ArgumentList.Add(sessionToken);
                Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
            }
            else
            {
                startInfo.ArgumentList.Add("--auth-mode");
                startInfo.ArgumentList.Add("offline");
                startInfo.ArgumentList.Add("--uuid");
                startInfo.ArgumentList.Add(sessionUuid);
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            Logger.Info("Game", $"Windows launch args: {string.Join(" ", startInfo.ArgumentList)}");
        }
        else
        {
            // Build arguments for the launch script
            var gameArgs = new List<string>
            {
                $"--app-dir \"{gameDir}\"",
                $"--user-dir \"{userDataDir}\"",
                $"--java-exec \"{javaPath}\"",
                $"--name \"{_config.Nick}\""
            };
            
            if (_config.OnlineMode && !string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
            {
                gameArgs.Add("--auth-mode authenticated");
                gameArgs.Add($"--uuid \"{sessionUuid}\"");
                gameArgs.Add($"--identity-token \"{identityToken}\"");
                gameArgs.Add($"--session-token \"{sessionToken}\"");
                Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
            }
            else
            {
                gameArgs.Add("--auth-mode offline");
                gameArgs.Add($"--uuid \"{sessionUuid}\"");
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            string argsString = string.Join(" ", gameArgs);
            string launchScript = Path.Combine(versionPath, "launch.sh");
            
            string homeDir = Environment.GetEnvironmentVariable("HOME") ?? "/Users/" + Environment.UserName;
            string userName = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
            
            string clientDir = Path.Combine(versionPath, "Client");
            
            string scriptContent = $@"#!/bin/bash
# Launch script generated by HyPrism
# Uses env to clear ALL environment variables before launching game

# Set LD_LIBRARY_PATH to include Client directory for shared libraries
CLIENT_DIR=""{clientDir}""

exec env \
    HOME=""{homeDir}"" \
    USER=""{userName}"" \
    PATH=""/usr/bin:/bin:/usr/sbin:/sbin:/usr/local/bin"" \
    SHELL=""/bin/zsh"" \
    TMPDIR=""{Path.GetTempPath().TrimEnd('/')}"" \
    LD_LIBRARY_PATH=""$CLIENT_DIR:$LD_LIBRARY_PATH"" \
    ""{executable}"" {argsString}
";
            File.WriteAllText(launchScript, scriptContent);
            
            // Make it executable
            var chmod = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{launchScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
            
            startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            startInfo.ArgumentList.Add(launchScript);
            
            Logger.Info("Game", $"Launch script: {launchScript}");
        }

        try
        {
            var process = Process.Start(startInfo);
            
            if (process != null)
            {
                _gameProcessService.SetGameProcess(process);
                Logger.Success("Game", $"Game started with PID: {process.Id}");
                
                _discordService.SetPresence(DiscordService.PresenceState.Playing, $"Playing as {_config.Nick}");
                _progressService.ReportGameStateChanged("started", process.Id);
                
                // Handle process exit in background
                _ = Task.Run(async () =>
                {
                    await process.WaitForExitAsync();
                    var exitCode = process.ExitCode;
                    Logger.Info("Game", $"Game process exited with code: {exitCode}");
                    _gameProcessService.SetGameProcess(null);
                    
                    _skinService.StopSkinProtection();
                    _skinService.BackupProfileSkinData(_userIdentityService.GetUuidForUser(_config.Nick));
                    
                    _discordService.SetPresence(DiscordService.PresenceState.Idle);
                    _progressService.ReportGameStateChanged("stopped", exitCode);
                });
            }
            else
            {
                Logger.Error("Game", "Process.Start returned null - game failed to launch");
                _progressService.ReportError("launch", "Failed to start game", "Process.Start returned null");
                throw new Exception("Failed to start game process");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Failed to start game process: {ex.Message}");
            _progressService.ReportError("launch", "Failed to start game", ex.Message);
            throw new Exception($"Failed to start game: {ex.Message}");
        }
    }
}
