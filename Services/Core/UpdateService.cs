using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Game;

namespace HyPrism.Services.Core;

/// <summary>
/// Manages HyPrism launcher updates via GitHub Releases.
/// Supports release (stable) and beta (pre-release) update channels.
/// </summary>
/// <remarks>
/// Checks GitHub releases API for new versions and handles the download,
/// extraction, and restart process for self-updating.
/// </remarks>
public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/yyyumeniku/HyPrism/releases";
    private const string ReleasesPageUrl = "https://github.com/yyyumeniku/HyPrism/releases/latest";
    
    private static readonly Lazy<string> _launcherVersion = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        
        if (!string.IsNullOrEmpty(infoVersion))
        {
            // Remove build metadata (e.g., "+abc123" suffix) if present
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }
        
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    });
    
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;
    private readonly VersionService _versionService;
    private readonly InstanceService _instanceService;
    private readonly BrowserService _browserService;
    private readonly ProgressNotificationService _progressNotificationService;
    
    /// <summary>
    /// Raised when a launcher update is available.
    /// </summary>
    public event Action<object>? LauncherUpdateAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for API requests.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="versionService">The version service for version checks.</param>
    /// <param name="instanceService">The instance service for path management.</param>
    /// <param name="browserService">The browser service for opening URLs.</param>
    /// <param name="progressNotificationService">The progress notification service.</param>
    public UpdateService(
        HttpClient httpClient,
        ConfigService configService,
        VersionService versionService,
        InstanceService instanceService,
        BrowserService browserService,
        ProgressNotificationService progressNotificationService)
    {
        _httpClient = httpClient;
        _configService = configService;
        _versionService = versionService;
        _instanceService = instanceService;
        _browserService = browserService;
        _progressNotificationService = progressNotificationService;
    }

    private Config _config => _configService.Configuration;

    /// <summary>
    /// Gets the path to the latest game instance for the current branch.
    /// </summary>
    /// <returns>The path to the latest instance directory.</returns>
    private string GetLatestInstancePath()
    {
        var branch = UtilityService.NormalizeVersionType(_config.VersionType);
        var info = _instanceService.LoadLatestInfo(branch);
        if (info != null)
        {
            return _instanceService.ResolveInstancePath(branch, info.Version, true);
        }
        return _instanceService.ResolveInstancePath(branch, 0, true);
    }

    #region Public API

    /// <summary>
    /// Возвращает текущую версию лаунчера.
    /// </summary>
    public string GetLauncherVersion() => _launcherVersion.Value;

    /// <summary>
    /// Возвращает текущий канал обновлений (release/beta).
    /// </summary>
    public string GetLauncherBranch() => 
        string.IsNullOrWhiteSpace(_config.LauncherBranch) ? "release" : _config.LauncherBranch;

    /// <summary>
    /// Проверяет наличие обновлений лаунчера на GitHub.
    /// При наличии вызывает событие LauncherUpdateAvailable.
    /// </summary>
    public async Task CheckForLauncherUpdatesAsync()
    {
        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            
            // Get all releases (not just latest) to support beta channel
            var apiUrl = $"{GitHubApiUrl}?per_page=50";
            var json = await _httpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);

            var currentVersion = GetLauncherVersion();
            string? bestVersion = null;
            JsonElement? bestRelease = null;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName)) continue;
                
                // Check GitHub's native prerelease flag
                var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseVal) && prereleaseVal.GetBoolean();
                
                // Match channel: beta channel gets prereleases, stable gets stable releases
                if (isBetaChannel && !isPrerelease)
                {
                    // User wants beta, skip stable releases
                    continue;
                }
                else if (!isBetaChannel && isPrerelease)
                {
                    // User wants stable, skip prereleases
                    continue;
                }
                
                // Parse version from tag
                var version = ParseVersionFromTag(tagName);
                if (string.IsNullOrWhiteSpace(version)) continue;
                
                // Compare with current version
                if (IsNewerVersion(version, currentVersion))
                {
                    // Check if this is better than our current best
                    if (bestVersion == null || IsNewerVersion(version, bestVersion))
                    {
                        bestVersion = version;
                        bestRelease = release;
                    }
                }
            }

            if (bestRelease.HasValue && !string.IsNullOrWhiteSpace(bestVersion))
            {
                var release = bestRelease.Value;
                Logger.Info("Update", $"Update available: {currentVersion} -> {bestVersion} (channel: {launcherBranch})");
                
                // Pick the right asset for this platform
                string? downloadUrl = null;
                string? assetName = null;
                var assets = release.GetProperty("assets");
                var arch = RuntimeInformation.ProcessArchitecture;

                string? targetAsset = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    targetAsset = "macos-arm64.dmg";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    targetAsset = "windows-x64.exe";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    targetAsset = arch == Architecture.Arm64 ? "linux-arm64.tar.gz" : "linux-x64.AppImage";

                if (!string.IsNullOrWhiteSpace(targetAsset))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(name) && name.Contains(targetAsset, StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            assetName = name;
                            break;
                        }
                    }
                }

                var updateInfo = new
                {
                    version = bestVersion,
                    currentVersion = currentVersion,
                    downloadUrl = downloadUrl ?? "",
                    assetName = assetName ?? "",
                    releaseUrl = release.GetProperty("html_url").GetString() ?? "",
                    isBeta = launcherBranch == "beta"
                };
                    
                LauncherUpdateAvailable?.Invoke(updateInfo);
            }
            else
            {
                Logger.Info("Update", $"Launcher is up to date: {currentVersion} (channel: {launcherBranch})");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Error checking for updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Скачивает и устанавливает обновление лаунчера.
    /// После успешной загрузки автоматически заменяет текущий файл и перезапускает лаунчер.
    /// </summary>
    public async Task<bool> UpdateAsync(JsonElement[]? args)
    {
        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            var currentVersion = GetLauncherVersion();
            
            // Get all releases to find the best match for user's channel
            var apiUrl = $"{GitHubApiUrl}?per_page=50";
            var json = await _httpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);
            
            // Find the best release for the user's channel
            JsonElement? targetRelease = null;
            string? targetVersion = null;
            
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseVal) && prereleaseVal.GetBoolean();
                
                // Match channel
                if (isBetaChannel && !isPrerelease) continue; // Beta wants prereleases
                if (!isBetaChannel && isPrerelease) continue; // Stable wants releases
                
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName)) continue;
                
                var version = ParseVersionFromTag(tagName);
                if (string.IsNullOrWhiteSpace(version)) continue;
                
                // Take the first matching release (they're sorted newest first)
                if (targetRelease == null)
                {
                    targetRelease = release;
                    targetVersion = version;
                    break;
                }
            }
            
            if (!targetRelease.HasValue || string.IsNullOrWhiteSpace(targetVersion))
            {
                Logger.Error("Update", $"No suitable {(isBetaChannel ? "pre-release" : "release")} found");
                _browserService.OpenURL(ReleasesPageUrl);
                return false;
            }
            
            Logger.Info("Update", $"Downloading {(isBetaChannel ? "pre-release" : "release")} {targetVersion} (current: {currentVersion})");
            
            var assets = targetRelease.Value.GetProperty("assets");

            // Pick asset by platform/arch
            string? targetAsset = null;
            var arch = RuntimeInformation.ProcessArchitecture;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                targetAsset = "macos-arm64.dmg"; // Apple Silicon only
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                targetAsset = "windows-x64.exe";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                targetAsset = arch == Architecture.Arm64 ? "linux-arm64.tar.gz" : "linux-x64.AppImage";
            }

            if (string.IsNullOrWhiteSpace(targetAsset))
            {
                Logger.Warning("Update", "Unsupported OS for auto-download, opening releases page");
                _browserService.OpenURL(ReleasesPageUrl);
                return false;
            }

            string? downloadUrl = null;
            string? assetName = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (!string.IsNullOrWhiteSpace(name) && name.Contains(targetAsset, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
            {
                Logger.Error("Update", "Could not find matching asset in latest release; opening releases page");
                _browserService.OpenURL(ReleasesPageUrl);
                return false;
            }

            var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloadsDir);
            var targetPath = Path.Combine(downloadsDir, assetName);

            Logger.Info("Update", $"Downloading latest launcher to {targetPath}");
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                }
            }

            // Platform-specific installation
            await InstallUpdateAsync(targetPath);
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Update failed: {ex.Message}");
            _browserService.OpenURL(ReleasesPageUrl);
            return false;
        }
    }

    /// <summary>
    /// Принудительно сбрасывает версию latest instance для триггера обновления игры.
    /// </summary>
    public async Task<bool> ForceUpdateLatestAsync(string branch)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var versions = await _versionService.GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return false;

            var info = _instanceService.LoadLatestInfo(normalizedBranch);
            
            if (info == null)
            {
                // No version info, assume version 1 to force full update path
                _instanceService.SaveLatestInfo(normalizedBranch, 1);
                Logger.Info("Update", $"No version info found, set to v1 to force update");
            }
            else
            {
                // Set installed version to one less than latest to trigger update
                int latestVersion = versions[0];
                if (info.Version < latestVersion)
                {
                    // Already behind, just return true
                    Logger.Info("Update", $"Already needs update: v{info.Version} -> v{latestVersion}");
                    return true;
                }
                // If somehow at or ahead of latest, force update by going back one version
                int forcedVersion = Math.Max(1, latestVersion - 1);
                _instanceService.SaveLatestInfo(normalizedBranch, forcedVersion);
                Logger.Info("Update", $"Forced version to v{forcedVersion} to trigger update to v{latestVersion}");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Failed to force update: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Duplicates the current latest instance as a versioned instance.
    /// Creates a copy with the current version number.
    /// </summary>
    public async Task<bool> DuplicateLatestAsync(string branch)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var info = _instanceService.LoadLatestInfo(normalizedBranch);
            
            if (info == null)
            {
                Logger.Warning("Update", "Cannot duplicate latest: no version info found");
                return false;
            }
            
            var currentVersion = info.Version;
            var latestPath = GetLatestInstancePath();
            
            if (!_instanceService.IsClientPresent(latestPath))
            {
                Logger.Warning("Update", "Cannot duplicate latest: instance not found");
                return false;
            }
            
            // Get versioned instance path
            var versionedPath = _instanceService.ResolveInstancePath(normalizedBranch, currentVersion, true);
            
            // Check if this version already exists
            if (_instanceService.IsClientPresent(versionedPath))
            {
                Logger.Warning("Update", $"Version {currentVersion} already exists, skipping duplicate");
                return false;
            }
            
            // Copy the entire latest instance folder to versioned folder
            Logger.Info("Update", $"Duplicating latest (v{currentVersion}) to versioned instance...");
            UtilityService.CopyDirectory(latestPath, versionedPath);
            
            // Save version info for the duplicated instance
            var versionInfoPath = Path.Combine(versionedPath, "version.json");
            var versionInfo = new { Version = currentVersion, Branch = normalizedBranch };
            File.WriteAllText(versionInfoPath, System.Text.Json.JsonSerializer.Serialize(versionInfo));
            
            Logger.Success("Update", $"Duplicated latest to versioned instance v{currentVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Failed to duplicate latest: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Platform-Specific Installation

    private async Task InstallUpdateAsync(string targetPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await InstallMacOSUpdateAsync(targetPath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            InstallWindowsUpdate(targetPath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InstallLinuxUpdate(targetPath);
        }
    }

    private async Task InstallMacOSUpdateAsync(string dmgPath)
    {
        try
        {
            Logger.Info("Update", "Mounting DMG and installing...");
            
            // Mount the DMG
            var mountProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "hdiutil",
                Arguments = $"attach \"{dmgPath}\" -nobrowse -readonly",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            if (mountProcess == null)
            {
                throw new Exception("Failed to mount DMG");
            }
            
            await mountProcess.WaitForExitAsync();
            var mountOutput = await mountProcess.StandardOutput.ReadToEndAsync();
            
            // Parse mount point from hdiutil output (last line, last column)
            var mountPoint = mountOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Split('\t', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Trim();
            
            if (string.IsNullOrWhiteSpace(mountPoint) || !Directory.Exists(mountPoint))
            {
                throw new Exception($"Could not find mount point. Output: {mountOutput}");
            }
            
            Logger.Info("Update", $"DMG mounted at: {mountPoint}");
            
            // Find the .app in the mounted DMG
            var appInDmg = Directory.GetDirectories(mountPoint, "*.app").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(appInDmg) || !Directory.Exists(appInDmg))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception("No .app found in DMG");
            }
            
            // Get current app path
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception("Could not determine current executable path");
            }
            
            // Navigate up to get the .app bundle path
            var currentAppPath = currentExe;
            for (int i = 0; i < 3; i++) // Go up 3 levels to get to .app
            {
                currentAppPath = Path.GetDirectoryName(currentAppPath);
                if (string.IsNullOrEmpty(currentAppPath)) break;
            }
            
            if (string.IsNullOrEmpty(currentAppPath) || !currentAppPath.EndsWith(".app"))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception($"Could not determine .app path from: {currentExe}");
            }
            
            Logger.Info("Update", $"Current app: {currentAppPath}");
            Logger.Info("Update", $"New app: {appInDmg}");
            
            // Create update script to replace app and restart
            var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
rm -rf ""{currentAppPath}""
cp -R ""{appInDmg}"" ""{currentAppPath}""
hdiutil detach ""{mountPoint}"" -force
rm -f ""{dmgPath}""
open ""{currentAppPath}""
rm -f ""$0""
";
            
            File.WriteAllText(updateScript, scriptContent);
            Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
            
            // Start the update script and exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{updateScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            Logger.Info("Update", "Update script started, exiting launcher...");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Auto-update failed: {ex.Message}");
            try { Process.Start("open", dmgPath); } catch { }
            throw new Exception($"Please install the update manually from Downloads. {ex.Message}");
        }
    }

    private void InstallWindowsUpdate(string exePath)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                Logger.Error("Update", "Could not determine current executable path");
                Process.Start("explorer.exe", $"/select,\"{exePath}\"");
                return;
            }

            // Create a batch script to replace the exe and restart
            var batchPath = Path.Combine(Path.GetTempPath(), "hyprism_update.bat");
            var batchContent = $@"@echo off
timeout /t 2 /nobreak >nul
del ""{currentExe}"" 2>nul
move /y ""{exePath}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""
";
            File.WriteAllText(batchPath, batchContent);

            // Start the batch script and exit
            var psi = new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            Logger.Info("Update", "Starting update script and exiting...");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Auto-update failed, opening Explorer: {ex.Message}");
            Process.Start("explorer.exe", $"/select,\"{exePath}\"");
        }
    }

    private void InstallLinuxUpdate(string targetPath)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                throw new Exception("Could not determine current executable path");
            }
            
            // For AppImage, just replace the file
            if (targetPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            {
                // Make the new AppImage executable
                Process.Start("chmod", $"+x \"{targetPath}\"")?.WaitForExit();
                
                // Create update script
                var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
                var scriptContent = $@"#!/bin/bash
sleep 2
rm -f ""{currentExe}""
mv ""{targetPath}"" ""{currentExe}""
chmod +x ""{currentExe}""
""{currentExe}"" &
rm -f ""$0""
";
                File.WriteAllText(updateScript, scriptContent);
                Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
                
                // Start the update script and exit
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{updateScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                Logger.Info("Update", "Update script started, exiting launcher...");
                Environment.Exit(0);
            }
            else
            {
                // For other formats, just open the file manager
                try { Process.Start("xdg-open", Path.GetDirectoryName(targetPath) ?? ""); } catch { }
                throw new Exception("Please install the update manually from Downloads.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Auto-update failed: {ex.Message}");
            try { Process.Start("xdg-open", targetPath); } catch { }
            throw new Exception($"Please install the update manually from Downloads. {ex.Message}");
        }
    }

    #endregion

    #region Version Parsing

    private static string? ParseVersionFromTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        
        var tagLower = tag.ToLowerInvariant();
        
        // Handle beta format: "beta3-3.0.0" or "beta-3.0.0"
        if (tagLower.StartsWith("beta"))
        {
            var dashIndex = tag.IndexOf('-');
            if (dashIndex >= 0 && dashIndex < tag.Length - 1)
            {
                var versionPart = tag.Substring(dashIndex + 1).TrimStart('v', 'V');
                return versionPart;
            }
            // beta without dash, try to extract version after "beta" text
            var afterBeta = tag.Substring(4).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimStart('-', '_').TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(afterBeta) ? null : afterBeta;
        }
        
        // Handle standard format: "v2.0.1" or "2.0.1"
        return tag.TrimStart('v', 'V');
    }

    private static bool IsNewerVersion(string remote, string current)
    {
        // Parse versions like "2.0.1" into comparable parts
        var remoteParts = remote.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var currentParts = current.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        for (int i = 0; i < Math.Max(remoteParts.Length, currentParts.Length); i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }
        return false;
    }

    #endregion

    #region Wrapper Mode

    /// <summary>
    /// Wrapper Mode: Get status of the installed HyPrism binary and check for updates.
    /// Returns: { installed: bool, version: string, needsUpdate: bool, latestVersion: string }
    /// </summary>
    public async Task<Dictionary<string, object>> WrapperGetStatus()
    {
        var result = new Dictionary<string, object>
        {
            ["installed"] = false,
            ["version"] = "",
            ["needsUpdate"] = false,
            ["latestVersion"] = ""
        };

        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, "HyPrism");
            var versionFile = Path.Combine(wrapperDir, "version.txt");

            if (!File.Exists(binaryPath))
            {
                return result;
            }

            result["installed"] = true;

            if (File.Exists(versionFile))
            {
                result["version"] = (await File.ReadAllTextAsync(versionFile)).Trim();
            }

            // Check GitHub for latest release
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (!string.IsNullOrEmpty(latestVersion))
            {
                result["latestVersion"] = latestVersion;
                result["needsUpdate"] = result["version"].ToString() != latestVersion;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperGetStatus error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Wrapper Mode: Install or update the latest HyPrism binary from GitHub releases.
    /// Downloads the appropriate release for the current OS and extracts it to wrapper directory.
    /// </summary>
    public async Task<bool> WrapperInstallLatest()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            Directory.CreateDirectory(wrapperDir);

            // Get latest release from GitHub
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (string.IsNullOrEmpty(latestVersion))
            {
                Console.WriteLine("Failed to get latest version from GitHub");
                return false;
            }

            // Determine the asset name based on OS
            string assetName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                assetName = $"HyPrism-{latestVersion}-linux-x64.tar.gz";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                assetName = $"HyPrism-{latestVersion}-win-x64.zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                assetName = $"HyPrism-{latestVersion}-osx-x64.tar.gz";
            }
            else
            {
                Console.WriteLine($"Unsupported platform: {RuntimeInformation.OSDescription}");
                return false;
            }

            var downloadUrl = $"https://github.com/yyyumeniku/HyPrism/releases/download/{latestVersion}/{assetName}";
            var archivePath = Path.Combine(wrapperDir, assetName);

            // Download archive
            _progressNotificationService.SendProgress("wrapper-install", 0, "Downloading HyPrism...", null, 0, 100);
            
            var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download: {response.StatusCode}");
                return false;
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(archivePath))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            _progressNotificationService.SendProgress("wrapper-install", 50, "Extracting...", null, 50, 100);

            // Extract archive
            if (assetName.EndsWith(".tar.gz"))
            {
                await ExtractTarGz(archivePath, wrapperDir);
            }
            else if (assetName.EndsWith(".zip"))
            {
                ZipFile.ExtractToDirectory(archivePath, wrapperDir, true);
            }

            // Set executable permission on Linux/Mac
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binaryPath = Path.Combine(wrapperDir, "HyPrism");
                if (File.Exists(binaryPath))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{binaryPath}\"",
                            UseShellExecute = false
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
            }

            // Save version
            await File.WriteAllTextAsync(Path.Combine(wrapperDir, "version.txt"), latestVersion);

            // Cleanup archive
            File.Delete(archivePath);

            _progressNotificationService.SendProgress("wrapper-install", 100, "Installation complete", null, 100, 100);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperInstallLatest error: {ex.Message}");
            _progressNotificationService.SendErrorEvent("Wrapper Installation Error", ex.Message, null);
            return false;
        }
    }

    /// <summary>
    /// Wrapper Mode: Launch the installed HyPrism binary.
    /// </summary>
    public async Task<bool> WrapperLaunch()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HyPrism.exe" : "HyPrism");

            if (!File.Exists(binaryPath))
            {
                Console.WriteLine("HyPrism binary not found");
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    UseShellExecute = true,
                    WorkingDirectory = wrapperDir
                }
            };

            process.Start();
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperLaunch error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Helper: Extract .tar.gz archive (for Linux/Mac releases).
    /// </summary>
    private static async Task ExtractTarGz(string archivePath, string destinationDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destinationDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Failed to extract tar.gz: {error}");
        }
    }

    /// <summary>
    /// Helper: Get latest launcher version from GitHub releases API.
    /// </summary>
    private async Task<string> GetLatestLauncherVersionFromGitHub()
    {
        try
        {
            var response = await _httpClient.GetStringAsync("https://api.github.com/repos/yyyumeniku/HyPrism/releases/latest");
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagName))
            {
                return tagName.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get latest version: {ex.Message}");
        }
        return "";
    }

    #endregion
}
