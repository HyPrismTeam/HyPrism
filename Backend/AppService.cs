using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Photino.NET;

namespace HyPrism.Backend;

public class AppService : IDisposable
{
    private readonly string _configPath;
    private readonly string _appDir;
    private Config _config;
    private Process? _gameProcess;
    private readonly ButlerService _butlerService;
    private readonly DiscordService _discordService;
    private CancellationTokenSource? _downloadCts;
    private bool _disposed;
    private PhotinoWindow? _mainWindow;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static AppService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism/1.0");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public AppService()
    {
        _appDir = GetDefaultAppDir();
        Directory.CreateDirectory(_appDir);
        _configPath = Path.Combine(_appDir, "config.json");
        _config = LoadConfig();
        _butlerService = new ButlerService(_appDir);
        _discordService = new DiscordService();
        _discordService.Initialize();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _discordService?.Dispose();
    }
    
    private string GetDefaultAppDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyPrism");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HyPrism");
        }
        else
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                return Path.Combine(xdgDataHome, "HyPrism");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "HyPrism");
        }
    }

    private static string GetOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "unknown";
    }

    private static string GetArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };
    }

    private int ResolveVersionOrLatest(string branch, int version)
    {
        if (version > 0) return version;
        if (_config.SelectedVersion > 0) return _config.SelectedVersion;

        var info = LoadLatestInfo(branch);
        if (info?.Version > 0) return info.Version;

        string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
        string branchDir = GetBranchPath(resolvedBranch);
        if (Directory.Exists(branchDir))
        {
            var latest = Directory.GetDirectories(branchDir)
                .Select(Path.GetFileName)
                .Select(name => int.TryParse(name, out var v) ? v : -1)
                .Where(v => v > 0)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            return latest;
        }

        return 0;
    }

    // Normalize version type: "prerelease" or "pre-release" -> "pre-release"
    private static string NormalizeVersionType(string versionType)
    {
        if (string.IsNullOrWhiteSpace(versionType))
            return "release";
        if (versionType == "prerelease" || versionType == "pre-release")
            return "pre-release";
        if (versionType == "latest")
            return "release";
        return versionType;
    }

    private sealed class LatestInstanceInfo
    {
        public int Version { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private string GetInstanceRoot()
    {
        var root = string.IsNullOrWhiteSpace(_config.InstanceDirectory)
            ? Path.Combine(_appDir, "instance")
            : _config.InstanceDirectory;

        root = Environment.ExpandEnvironmentVariables(root);

        if (!Path.IsPathRooted(root))
        {
            root = Path.GetFullPath(Path.Combine(_appDir, root));
        }

        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to create instance root at {root}: {ex.Message}");
        }

        return root;
    }

    private string GetBranchPath(string branch)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        return Path.Combine(GetInstanceRoot(), normalizedBranch);
    }

    private string GetLatestInstancePath(string branch)
    {
        return Path.Combine(GetBranchPath(branch), "latest");
    }

    private string GetLatestInfoPath(string branch)
    {
        return Path.Combine(GetLatestInstancePath(branch), "latest.json");
    }

    private LatestInstanceInfo? LoadLatestInfo(string branch)
    {
        try
        {
            var path = GetLatestInfoPath(branch);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LatestInstanceInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveLatestInfo(string branch, int version)
    {
        try
        {
            Directory.CreateDirectory(GetBranchPath(branch));
            var info = new LatestInstanceInfo { Version = version, UpdatedAt = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            File.WriteAllText(GetLatestInfoPath(branch), json);
        }
        catch
        {
            // ignore
        }
    }

    private string GetInstancePath(string branch, int version)
    {
        if (version == 0)
        {
            return GetLatestInstancePath(branch);
        }
        string normalizedBranch = NormalizeVersionType(branch);
        return Path.Combine(GetInstanceRoot(), normalizedBranch, version.ToString());
    }

    private async Task<(string branch, int version)> ResolveLatestCompositeAsync()
    {
        var releaseVersions = await GetVersionListAsync("release");
        var preVersions = await GetVersionListAsync("pre-release");
        int releaseLatest = releaseVersions.FirstOrDefault();
        int preLatest = preVersions.FirstOrDefault();

        // If both missing, default to release 0
        if (releaseLatest == 0 && preLatest == 0)
        {
            return ("release", 0);
        }

        // Prefer whichever has the higher version number; tie goes to pre-release
        if (preLatest >= releaseLatest)
        {
            return ("pre-release", preLatest);
        }

        return ("release", releaseLatest);
    }

    private Config LoadConfig()
    {
        Config config;
        
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                
                // Migration: Ensure UUID exists
                if (string.IsNullOrEmpty(config.UUID))
                {
                    config.UUID = Guid.NewGuid().ToString();
                    config.Version = "2.0.0";
                    SaveConfigInternal(config);
                    Logger.Info("Config", $"Migrated to v2.0.0, UUID: {config.UUID}");
                }
                
                return config;
            }
            catch
            {
                config = new Config();
            }
        }
        else
        {
            config = new Config();
        }
        
        // New config - generate UUID
        if (string.IsNullOrEmpty(config.UUID))
        {
            config.UUID = Guid.NewGuid().ToString();
        }

        // Default nick to Hyprism if empty or placeholder
        if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player")
        {
            config.Nick = "Hyprism";
        }

        // Migrate legacy "latest" branch to release
        if (config.VersionType == "latest")
        {
            config.VersionType = "release";
        }
        SaveConfigInternal(config);
        return config;
    }

    private void SaveConfigInternal(Config config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private void SaveConfig()
    {
        SaveConfigInternal(_config);
    }

    // Config
    public Config QueryConfig() => _config;

    public string GetNick() => _config.Nick;
    
    public string GetUUID() => _config.UUID;

    public string GetCustomInstanceDir() => _config.InstanceDirectory ?? "";

    public bool SetUUID(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return false;
        if (!Guid.TryParse(uuid.Trim(), out var parsed)) return false;
        _config.UUID = parsed.ToString();
        SaveConfig();
        return true;
    }
    
    public bool SetNick(string nick)
    {
        _config.Nick = nick;
        SaveConfig();
        return true;
    }

    public async Task<string?> SetInstanceDirectoryAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(Path.Combine(_appDir, expanded));
            }

            Directory.CreateDirectory(expanded);

            _config.InstanceDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Instance directory set to {expanded}");
            return expanded;
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
            return null;
        }
    }

    public string GetLauncherVersion() => "2.0.0";

    // Version Management
    public string GetVersionType() => _config.VersionType;
    
    public bool SetVersionType(string versionType)
    {
        _config.VersionType = NormalizeVersionType(versionType);
        SaveConfig();
        return true;
    }

    // Returns list of available version numbers by checking Hytale's patch server
    public async Task<List<int>> GetVersionListAsync(string branch)
    {
        var normalizedBranch = NormalizeVersionType(branch);

        var result = new List<int>();
        string osName = GetOS();
        string arch = GetArch();
        string apiVersionType = normalizedBranch;

        // Start version depends on branch type
        int startVersion = apiVersionType == "pre-release" ? 10 : 5;

        // Check versions in parallel
        var tasks = new List<Task<(int version, bool exists)>>();
        
        for (int v = 1; v <= startVersion; v++)
        {
            int version = v;
            tasks.Add(CheckVersionExistsAsync(osName, arch, apiVersionType, version));
        }

        var results = await Task.WhenAll(tasks);
        
        foreach (var (version, exists) in results)
        {
            if (exists)
            {
                result.Add(version);
            }
        }

        result.Sort((a, b) => b.CompareTo(a)); // Sort descending (latest first)
        Logger.Info("Version", $"Found {result.Count} versions for {branch}: [{string.Join(", ", result)}]");
        return result;
    }

    private async Task<(int version, bool exists)> CheckVersionExistsAsync(string os, string arch, string versionType, int version)
    {
        try
        {
            string url = $"https://game-patches.hytale.com/patches/{os}/{arch}/{versionType}/0/{version}.pwr";
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return (version, response.IsSuccessStatusCode);
        }
        catch
        {
            return (version, false);
        }
    }

    public bool SetSelectedVersion(int versionNumber)
    {
        _config.SelectedVersion = versionNumber;
        SaveConfig();
        return true;
    }

    public bool IsVersionInstalled(string branch, int versionNumber)
    {
        var normalizedBranch = NormalizeVersionType(branch);

        // Version 0 means "latest" - check if any version is installed
        if (versionNumber == 0)
        {
            var latestPath = GetLatestInstancePath(normalizedBranch);
            bool hasClient = IsClientPresent(latestPath);
            Logger.Info("Version", $"IsVersionInstalled check for version 0 (latest): path={latestPath}, hasClient={hasClient}");
            return hasClient;
        }
        
        string versionPath = Path.Combine(GetInstanceRoot(), normalizedBranch, versionNumber.ToString());

        if (!IsClientPresent(versionPath))
        {
            return false;
        }

        return true;
    }

    private bool IsClientPresent(string versionPath)
    {
        string clientPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            clientPath = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            clientPath = Path.Combine(versionPath, "Client", "HytaleClient.exe");
        }
        else
        {
            clientPath = Path.Combine(versionPath, "Client", "HytaleClient");
        }

        bool exists = File.Exists(clientPath);
        Logger.Info("Version", $"IsClientPresent: path={clientPath}, exists={exists}");
        return exists;
    }

    private bool AreAssetsPresent(string versionPath)
    {
        string assetsCheck;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsCheck = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets");
        }
        else
        {
            assetsCheck = Path.Combine(versionPath, "Client", "Assets");
        }

        bool exists = Directory.Exists(assetsCheck) && Directory.EnumerateFileSystemEntries(assetsCheck).Any();
        Logger.Info("Version", $"AreAssetsPresent: path={assetsCheck}, exists={exists}");
        return exists;
    }

    public List<int> GetInstalledVersionsForBranch(string branch)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var result = new List<int>();

        string branchPath = Path.Combine(GetInstanceRoot(), normalizedBranch);
        
        if (Directory.Exists(branchPath))
        {
            foreach (var dir in Directory.GetDirectories(branchPath))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsClientPresent(dir))
                    {
                        result.Add(0);
                    }
                    continue;
                }

                if (int.TryParse(name, out int version))
                {
                    if (IsClientPresent(dir))
                    {
                        result.Add(version);
                    }
                }
            }
        }
        
        return result;
    }

    public async Task<bool> CheckLatestNeedsUpdateAsync(string branch)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var versions = await GetVersionListAsync(normalizedBranch);
        if (versions.Count == 0) return false;

        var latest = versions[0];
        var latestPath = GetLatestInstancePath(normalizedBranch);
        var info = LoadLatestInfo(normalizedBranch);
        var baseOk = IsClientPresent(latestPath);
        if (!baseOk) return true;
        if (info == null)
        {
            SaveLatestInfo(normalizedBranch, latest);
            return false;
        }
        return info.Version != latest;
    }

    // Game
    public async Task<DownloadProgress> DownloadAndLaunchAsync(PhotinoWindow window)
    {
        try
        {
            _downloadCts = new CancellationTokenSource();
            
            string branch = NormalizeVersionType(_config.VersionType);
            var versions = await GetVersionListAsync(branch);
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

            string versionPath = isLatestInstance
                ? GetLatestInstancePath(branch)
                : Path.Combine(GetInstanceRoot(), branch, targetVersion.ToString());
            Directory.CreateDirectory(versionPath);

            // Check if we need to download/install - verify all components
            // The game is installed if the Client executable exists - that's all we need to check
            bool gameIsInstalled = IsClientPresent(versionPath);
            
            Logger.Info("Download", $"=== INSTALL CHECK ===");
            Logger.Info("Download", $"Version path: {versionPath}");
            Logger.Info("Download", $"Is latest instance: {isLatestInstance}");
            Logger.Info("Download", $"Target version: {targetVersion}");
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}");
            
            // If game is already installed, just launch it!
            if (gameIsInstalled)
            {
                Logger.Success("Download", "Game is already installed - skipping all downloads, launching directly!");
                
                // Just ensure JRE is available (download if needed, but don't touch the game)
                string jrePath = GetJavaPath();
                if (!File.Exists(jrePath))
                {
                    Logger.Info("Download", "JRE missing, installing...");
                    SendProgress(window, "install", 96, "Installing Java Runtime...", 0, 0);
                    try
                    {
                        await EnsureJREInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 96 + (int)(progress * 0.03);
                            SendProgress(window, "install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                        return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
                    }
                }
                
                // Update latest info if needed
                if (isLatestInstance)
                {
                    var info = LoadLatestInfo(branch);
                    if (info == null || info.Version != targetVersion)
                    {
                        SaveLatestInfo(branch, targetVersion);
                    }
                }
                
                SendProgress(window, "complete", 100, "Launching game...", 0, 0);
                await LaunchGameAsync(versionPath, branch);
                return new DownloadProgress { Success = true, Progress = 100 };
            }
            
            // Game is NOT installed - need to download
            Logger.Info("Download", "Game not installed, starting download...");

            SendProgress(window, "download", 0, "Preparing download...", 0, 0);
            
            // First, ensure Butler is installed (0-5% progress)
            try
            {
                await _butlerService.EnsureButlerInstalledAsync((progress, message) =>
                {
                    // Map butler install progress to 0-5%
                    int mappedProgress = (int)(progress * 0.05);
                    SendProgress(window, "download", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Butler install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
            }

            ThrowIfCancelled();
            
            // Download PWR file (5-70% progress)
            string osName = GetOS();
            string arch = GetArch();
            string apiVersionType = NormalizeVersionType(branch);
            string downloadUrl = $"https://game-patches.hytale.com/patches/{osName}/{arch}/{apiVersionType}/0/{targetVersion}.pwr";
            string pwrPath = Path.Combine(_appDir, "cache", $"{branch}_{(isLatestInstance ? "latest" : "version")}_{targetVersion}.pwr");
            
            Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);
            
            Logger.Info("Download", $"Downloading: {downloadUrl}");
            
            await DownloadFileAsync(downloadUrl, pwrPath, (progress, downloaded, total) =>
            {
                // Map download progress to 5-65%
                int mappedProgress = 5 + (int)(progress * 0.60);
                SendProgress(window, "download", mappedProgress, $"Downloading... {progress}%", downloaded, total);
            }, _downloadCts.Token);
            
            // Extract PWR file using Butler (65-85% progress)
            SendProgress(window, "install", 65, "Installing game with Butler...", 0, 0);
            
            try
            {
                await _butlerService.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
                {
                    // Map install progress (0-100) to 65-85%
                    int mappedProgress = 65 + (int)(progress * 0.20);
                    SendProgress(window, "install", mappedProgress, message, 0, 0);
                });
                
                // Clean up PWR file after successful extraction
                if (File.Exists(pwrPath))
                {
                    try { File.Delete(pwrPath); } catch { }
                }
                
                // Skip assets extraction on install to match legacy layout
                ThrowIfCancelled();
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
            }

            if (isLatestInstance)
            {
                SaveLatestInfo(branch, targetVersion);
            }
            
            SendProgress(window, "complete", 95, "Download complete!", 0, 0);

            // Ensure JRE is installed before launching
            SendProgress(window, "install", 96, "Checking Java Runtime...", 0, 0);
            try
            {
                await EnsureJREInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 96 + (int)(progress * 0.03); // 96-99%
                    SendProgress(window, "install", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
            }

            ThrowIfCancelled();

            SendProgress(window, "complete", 100, "Launching game...", 0, 0);

            // Launch the game
            await LaunchGameAsync(versionPath, branch);
            
            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Download cancelled");
            try
            {
                SendProgress(window, "cancelled", 0, "Cancelled", 0, 0);
            }
            catch { }
            return new DownloadProgress { Error = "Download cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Error: {ex.Message}");
            return new DownloadProgress { Error = ex.Message };
        }
        finally
        {
            _downloadCts = null;
        }
    }

    public bool CancelDownload()
    {
        if (_downloadCts != null)
        {
            _downloadCts.Cancel();
            return true;
        }
        return false;
    }

    private void ThrowIfCancelled()
    {
        if (_downloadCts?.IsCancellationRequested == true)
        {
            throw new OperationCanceledException();
        }
    }

    private void SendProgress(PhotinoWindow window, string stage, int progress, string message, long downloaded, long total)
    {
        var progressInfo = new 
        { 
            type = "event",
            eventName = "progress-update",
            data = new { stage, progress, message, downloaded, total }
        };
        window.SendWebMessage(JsonSerializer.Serialize(progressInfo, JsonOptions));
        
        // Don't update Discord during download/install to avoid showing extraction messages
        // Only update on complete or idle
        if (stage == "complete")
        {
            _discordService.SetPresence(DiscordService.PresenceState.Idle);
        }
    }

    private async Task DownloadFileAsync(string url, string path, Action<int, long, long> progressCallback, CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var canReportProgress = totalBytes > 0;
        
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        int lastReportedProgress = -1;
        
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            
            if (canReportProgress)
            {
                var progress = (int)((totalRead * 100) / totalBytes);
                // Only report if progress changed to reduce spam
                if (progress != lastReportedProgress)
                {
                    progressCallback(progress, totalRead, totalBytes);
                    lastReportedProgress = progress;
                }
            }
        }
    }

    // Extract Assets.zip to the correct location for macOS
    private async Task ExtractAssetsIfNeededAsync(string versionPath, Action<int, string> progressCallback)
    {
        // Check if Assets.zip exists
        string assetsZip = Path.Combine(versionPath, "Assets.zip");
        if (!File.Exists(assetsZip))
        {
            Logger.Info("Assets", "No Assets.zip found, skipping extraction");
            progressCallback(100, "No assets extraction needed");
            return;
        }
        
        // Determine target path based on OS
        string assetsDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsDir = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            assetsDir = Path.Combine(versionPath, "Client", "Assets");
        }
        else
        {
            assetsDir = Path.Combine(versionPath, "Client", "Assets");
        }
        
        // Check if already extracted
        if (Directory.Exists(assetsDir) && Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories).Length > 0)
        {
            Logger.Info("Assets", "Assets already extracted");
            progressCallback(100, "Assets ready");
            return;
        }
        
        Logger.Info("Assets", $"Extracting Assets.zip to {assetsDir}...");
        progressCallback(0, "Extracting game assets...");
        
        try
        {
            Directory.CreateDirectory(assetsDir);
            
            // Extract using ZipFile
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(assetsZip);
                var totalEntries = archive.Entries.Count;
                var extracted = 0;
                
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    
                    // Get relative path - Assets.zip may have "Assets/" prefix or not
                    var relativePath = entry.FullName;
                    if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(7);
                    }
                    else if (relativePath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(7);
                    }
                    
                    var destPath = Path.Combine(assetsDir, relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    entry.ExtractToFile(destPath, true);
                    extracted++;
                    
                    if (totalEntries > 0 && extracted % 100 == 0)
                    {
                        var progress = (int)((extracted * 100) / totalEntries);
                        progressCallback(progress, $"Extracting assets... {progress}%");
                    }
                }
            });
            
            // Optionally delete the zip after extraction to save space
            try { File.Delete(assetsZip); } catch { }
            
            // On macOS, create symlink at root level for game compatibility
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string rootAssetsLink = Path.Combine(versionPath, "Assets");
                
                try
                {
                    // Remove existing symlink/directory if it exists
                    if (Directory.Exists(rootAssetsLink) || File.Exists(rootAssetsLink))
                    {
                        try 
                        { 
                            // Check if it's a symlink
                            FileAttributes attrs = File.GetAttributes(rootAssetsLink);
                            if ((attrs & FileAttributes.ReparsePoint) != 0)
                            {
                                // It's a symlink - delete it
                                File.Delete(rootAssetsLink);
                                Logger.Info("Assets", "Removed existing Assets symlink");
                            }
                            else if (Directory.Exists(rootAssetsLink))
                            {
                                // It's a real directory - delete it
                                Directory.Delete(rootAssetsLink, true);
                                Logger.Info("Assets", "Removed existing Assets directory");
                            }
                        } 
                        catch (Exception ex)
                        {
                            Logger.Warning("Assets", $"Could not remove existing Assets: {ex.Message}");
                        }
                    }
                    
                    // Use relative path for symlink so it works even if directory moves
                    string relativeAssetsPath = "Client/Hytale.app/Contents/Assets";
                    
                    // Create symlink using ln command - run from version directory
                    var lnAssets = new ProcessStartInfo("ln", new[] { "-s", relativeAssetsPath, "Assets" })
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WorkingDirectory = versionPath
                    };
                    var lnProcess = Process.Start(lnAssets);
                    if (lnProcess != null)
                    {
                        string errors = await lnProcess.StandardError.ReadToEndAsync();
                        string output = await lnProcess.StandardOutput.ReadToEndAsync();
                        await lnProcess.WaitForExitAsync();
                        
                        if (lnProcess.ExitCode == 0)
                        {
                            Logger.Success("Assets", $"Created Assets symlink: {rootAssetsLink} -> {relativeAssetsPath}");
                            
                            // Verify the symlink works
                            if (Directory.Exists(rootAssetsLink))
                            {
                                Logger.Success("Assets", "Assets symlink verified - directory is accessible");
                            }
                            else
                            {
                                Logger.Error("Assets", "Assets symlink created but directory not accessible");
                            }
                        }
                        else
                        {
                            Logger.Error("Assets", $"Symlink creation failed with exit code {lnProcess.ExitCode}");
                            if (!string.IsNullOrEmpty(errors))
                            {
                                Logger.Error("Assets", $"Error output: {errors}");
                            }
                            if (!string.IsNullOrEmpty(output))
                            {
                                Logger.Info("Assets", $"Standard output: {output}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Assets", $"Failed to create Assets symlink: {ex.Message}");
                }
            }
            
            Logger.Success("Assets", "Assets extracted successfully");
            progressCallback(100, "Assets extracted");
        }
        catch (Exception ex)
        {
            Logger.Error("Assets", $"Failed to extract assets: {ex.Message}");
            throw;
        }
    }

    // JRE Download - uses jre.json config for direct GitHub download links
    private async Task EnsureJREInstalledAsync(Action<int, string> progressCallback)
    {
        string jreDir = Path.Combine(_appDir, "jre");
        string javaBin;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            javaBin = Path.Combine(jreDir, "bin", "java.exe");
        }
        else
        {
            javaBin = Path.Combine(jreDir, "bin", "java");
        }
        
        if (File.Exists(javaBin))
        {
            try
            {
                int featureVersion = await GetJavaFeatureVersionAsync(javaBin);
                bool supportsShenandoah = await SupportsShenandoahAsync(javaBin);
                if (supportsShenandoah && featureVersion >= 21)
                {
                    Logger.Info("JRE", $"Java Runtime already installed (feature {featureVersion}) and supports Shenandoah generational mode");
                    EnsureJavaWrapper(javaBin);
                    progressCallback(100, "Java Runtime ready");
                    return;
                }

                Logger.Warning("JRE", $"Installed Java (feature {featureVersion}) is missing required features. Reinstalling JRE 21...");
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to validate Java features: {ex.Message}. Reinstalling...");
            }
        }
        
        progressCallback(0, "Downloading Java Runtime...");
        Logger.Info("JRE", "Downloading Java Runtime...");
        
        // Determine platform
        string osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "mac" : 
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";
        string archiveType = osName == "windows" ? "zip" : "tar.gz";
        
        // Load JRE config from embedded JSON or fallback to API
        string? url = null;
        try
        {
            var jreConfigPath = Path.Combine(AppContext.BaseDirectory, "jre.json");
            if (File.Exists(jreConfigPath))
            {
                var jreConfigJson = await File.ReadAllTextAsync(jreConfigPath);
                var jreConfig = JsonSerializer.Deserialize<JsonElement>(jreConfigJson);
                
                if (jreConfig.TryGetProperty("temurin", out var temurin) &&
                    temurin.TryGetProperty(osName, out var osConfig) &&
                    osConfig.TryGetProperty(arch, out var archConfig))
                {
                    url = archConfig.GetString();
                    Logger.Info("JRE", $"Using JRE URL from config: {url}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to load jre.json config: {ex.Message}");
        }
        
        // Fallback to Adoptium API if JSON config not found
        if (string.IsNullOrEmpty(url))
        {
            url = $"https://api.adoptium.net/v3/binary/latest/21/ga/{osName}/{arch}/jre/hotspot/normal/eclipse?project=jdk";
            Logger.Info("JRE", $"Using Adoptium API fallback (JRE 21): {url}");
        }
        
        string cacheDir = Path.Combine(_appDir, "cache");
        Directory.CreateDirectory(cacheDir);
        string archivePath = Path.Combine(cacheDir, $"jre.{archiveType}");
        
        // Download with proper headers for Adoptium API
        using var jreClient = new HttpClient();
        jreClient.Timeout = TimeSpan.FromMinutes(10);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "HyPrism/1.0");
        request.Headers.Add("Accept", "*/*");
        
        using var response = await jreClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;
            
            if (totalBytes > 0)
            {
                var progress = (int)((totalRead * 80) / totalBytes); // 0-80%
                progressCallback(progress, $"Downloading Java Runtime... {progress}%");
            }
        }
        fileStream.Close();
        
        progressCallback(85, "Extracting Java Runtime...");
        Logger.Info("JRE", "Extracting Java Runtime...");
        
        // Create jre directory
        Directory.CreateDirectory(jreDir);
        
        // Extract
        if (archiveType == "zip")
        {
            ZipFile.ExtractToDirectory(archivePath, jreDir, true);
        }
        else
        {
            // Use tar on Unix systems
            var tarProcess = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{jreDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var tar = Process.Start(tarProcess);
            tar?.WaitForExit();
        }
        
        // Normalize JRE structure - move contents up if nested
        var entries = Directory.GetDirectories(jreDir);
        if (entries.Length == 1)
        {
            var subDir = entries[0];
            
            // On macOS, structure is different
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var contentsDir = Path.Combine(subDir, "Contents", "Home");
                if (Directory.Exists(contentsDir))
                {
                    subDir = contentsDir;
                }
            }
            
            // Move files from subdirectory to jreDir
            foreach (var entry in Directory.GetFileSystemEntries(subDir))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(jreDir, name);
                if (!File.Exists(dest) && !Directory.Exists(dest))
                {
                    Directory.Move(entry, dest);
                }
            }
            
            // Remove empty subdirectory
            try { Directory.Delete(entries[0], true); } catch { }
        }
        
        // Make java executable on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
        }
        
        // Cleanup archive
        try { File.Delete(archivePath); } catch { }
        
        // On macOS, create java symlink structure like old launcher
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await SetupMacOSJavaSymlinksAsync(jreDir);
        }

        // Wrap java to strip unsupported Shenandoah flags and point to the freshly installed JRE 21
        EnsureJavaWrapper(javaBin);
        
        progressCallback(100, "Java Runtime installed");
        Logger.Success("JRE", "Java Runtime installed successfully");
    }

    private async Task SetupMacOSJavaSymlinksAsync(string jreDir)
    {
        // Create java directory structure like old launcher
        string javaDir = Path.Combine(_appDir, "java");
        string javaHomeBin = Path.Combine(javaDir, "Contents", "Home", "bin");
        
        if (!Directory.Exists(javaHomeBin))
        {
            try
            {
                if (Directory.Exists(javaDir))
                {
                    Directory.Delete(javaDir, true);
                }
                
                Directory.CreateDirectory(Path.Combine(javaDir, "Contents", "Home"));
                
                // Create symlinks
                var lnBin = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "bin")}\" \"{Path.Combine(javaDir, "Contents", "Home", "bin")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(lnBin)?.WaitForExit();
                
                var lnLib = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "lib")}\" \"{Path.Combine(javaDir, "Contents", "Home", "lib")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(lnLib)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to create Java symlinks: {ex.Message}");
            }
        }
        
        // Sign JRE
        Logger.Info("JRE", "Signing Java Runtime...");
        RunSilentProcess("xattr", $"-cr \"{jreDir}\"");
        RunSilentProcess("codesign", $"--force --deep --sign - \"{jreDir}\"");
        await Task.CompletedTask;
    }

    private async Task<int> GetJavaFeatureVersionAsync(string javaBin)
    {
        try
        {
            var psi = new ProcessStartInfo(javaBin, "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return 0;
            }

            string stderr = await proc.StandardError.ReadToEndAsync();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            var match = Regex.Match(output, "version \"(?<ver>[^\" ]+)\"");
            if (match.Success)
            {
                return ParseJavaMajor(match.Groups["ver"].Value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to read Java version: {ex.Message}");
        }

        return 0;
    }

    private int ParseJavaMajor(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return 0;
        }

        var parts = versionString.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return 0;
        }

        if (int.TryParse(parts[0], out var major))
        {
            if (major == 1 && parts.Length > 1 && int.TryParse(parts[1], out var minor))
            {
                return minor;
            }

            return major;
        }

        return 0;
    }

    private async Task<bool> SupportsShenandoahAsync(string javaBin)
    {
        try
        {
            var psi = new ProcessStartInfo(javaBin, "-XX:+UseShenandoahGC -version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            string stderr = await proc.StandardError.ReadToEndAsync();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                return true;
            }

            var combined = (stdout + "\n" + stderr).ToLowerInvariant();
            if (combined.Contains("unrecognized") || combined.Contains("could not create the java virtual machine"))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Shenandoah probe failed: {ex.Message}");
        }

        return false;
    }

    private void EnsureJavaWrapper(string javaBin)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows already uses java.exe; wrapper not required.
            return;
        }

        try
        {
            var javaDir = Path.GetDirectoryName(javaBin);
            if (string.IsNullOrEmpty(javaDir))
            {
                return;
            }

            var realJava = Path.Combine(javaDir, "java.real");

            if (!File.Exists(realJava))
            {
                try
                {
                    if (File.Exists(javaBin))
                    {
                        // If javaBin is already a wrapper script, avoid moving it over realJava
                        byte[] headBytes = new byte[2];
                        using (var fs = new FileStream(javaBin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _ = fs.Read(headBytes, 0, 2);
                        }

                        bool looksLikeScript = headBytes[0] == (byte)'#' && headBytes[1] == (byte)'!';
                        if (looksLikeScript)
                        {
                            Logger.Warning("JRE", "Wrapper detected but java.real missing; skipping move to avoid clobbering wrapper");
                            return;
                        }

                        File.Move(javaBin, realJava, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("JRE", $"Failed to move java binary for wrapping: {ex.Message}");
                    return;
                }
            }

            var wrapper = "#!/bin/bash\n" +
                         "REAL_JAVA=\"$(cd \"$(dirname \"$0\")\" && pwd)/java.real\"\n" +
                         "ARGS=()\n" +
                         "for arg in \"$@\"; do\n" +
                         "  if [[ \"$arg\" == -XX:ShenandoahGCMode=* ]]; then\n" +
                         "    continue\n" +
                         "  fi\n" +
                         "  ARGS+=(\"$arg\")\n" +
                         "done\n" +
                         "exec \"$REAL_JAVA\" \"${ARGS[@]}\"\n";

            File.WriteAllText(javaBin, wrapper);
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to create Java wrapper: {ex.Message}");
        }
    }

    private void CleanupCorruptedInstall(string versionPath)
    {
        string backupRoot = Path.Combine(Path.GetTempPath(), "HyPrismBackup", Guid.NewGuid().ToString());
        // Preserve UserData and Client/Assets to avoid re-downloading game
        string[] preserve = { "UserData", "Client" };

        try
        {
            Directory.CreateDirectory(backupRoot);
            foreach (var dirName in preserve)
            {
                var src = Path.Combine(versionPath, dirName);
                if (Directory.Exists(src))
                {
                    var dest = Path.Combine(backupRoot, dirName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    CopyDirectory(src, dest);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to backup user data before cleanup: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to clean corrupted install at {versionPath}: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(versionPath);
            foreach (var dirName in preserve)
            {
                var backup = Path.Combine(backupRoot, dirName);
                var dest = Path.Combine(versionPath, dirName);
                if (Directory.Exists(backup))
                {
                    CopyDirectory(backup, dest);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to recreate install directory {versionPath}: {ex.Message}");
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private void RunSilentProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.Warning("Process", $"Failed to run {fileName} {arguments}: {ex.Message}");
        }
    }

    private bool IsMacAppSignatureCurrent(string executablePath, string stampPath)
    {
        try
        {
            if (!File.Exists(executablePath) || !File.Exists(stampPath))
            {
                return false;
            }

            var stamp = File.ReadAllText(stampPath).Trim();
            var currentTicks = File.GetLastWriteTimeUtc(executablePath).Ticks.ToString();
            return string.Equals(stamp, currentTicks, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void MarkMacAppSigned(string executablePath, string stampPath)
    {
        try
        {
            var ticks = File.GetLastWriteTimeUtc(executablePath).Ticks.ToString();
            File.WriteAllText(stampPath, ticks);
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to record app sign stamp: {ex.Message}");
        }
    }

    private void ClearMacQuarantine(string path)
    {
        try
        {
            RunSilentProcess("xattr", $"-cr \"{path}\"");
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to clear quarantine on {path}: {ex.Message}");
        }
    }

    private string GetJavaPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Use the symlinked java path on macOS like old launcher
            return Path.Combine(_appDir, "java", "Contents", "Home", "bin", "java");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(_appDir, "jre", "bin", "java.exe");
        }
        else
        {
            return Path.Combine(_appDir, "jre", "bin", "java");
        }
    }

    private async Task LaunchGameAsync(string versionPath, string branch)
    {
        Logger.Info("Game", $"Preparing to launch from {versionPath}");
        
        string executable;
        string workingDir;
        string gameDir = versionPath;
        
        // Determine client path based on OS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            executable = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            workingDir = Path.Combine(versionPath, "Client");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = Path.Combine(versionPath, "Client", "HytaleClient.exe");
            workingDir = Path.Combine(versionPath, "Client");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }
        else
        {
            // Linux
            executable = Path.Combine(versionPath, "Client", "HytaleClient");
            workingDir = Path.Combine(versionPath, "Client");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }

        // Get Java path
        string javaPath = GetJavaPath();
        if (!File.Exists(javaPath))
        {
            Logger.Error("Game", $"Java not found at {javaPath}");
            throw new Exception($"Java not found at {javaPath}");
        }
        
        // Verify Java is executable by running --version
        try
        {
            var javaCheck = new ProcessStartInfo(javaPath, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var javaProcess = Process.Start(javaCheck);
            if (javaProcess != null)
            {
                string javaOutput = await javaProcess.StandardOutput.ReadToEndAsync();
                await javaProcess.WaitForExitAsync();
                if (javaProcess.ExitCode == 0)
                {
                    Logger.Success("Game", $"Java verified: {javaOutput.Split('\n')[0]}");
                }
                else
                {
                    Logger.Warning("Game", $"Java check returned exit code {javaProcess.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Could not verify Java: {ex.Message}");
        }

        // Create UserData directory
        string userDataDir = Path.Combine(versionPath, "UserData");
        Directory.CreateDirectory(userDataDir);

        // Use saved UUID if available; fallback to offline UUID from name and save it
        string uuid;
        if (string.IsNullOrWhiteSpace(_config.UUID))
        {
            uuid = GenerateOfflineUUID(_config.Nick);
            _config.UUID = uuid;
            SaveConfig();
            Logger.Info("Config", $"Generated and saved UUID from nickname: {uuid}");
        }
        else
        {
            uuid = _config.UUID;
        }

        // On macOS, clear quarantine flags before launching (skip full codesign for speed)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
            string signStampPath = Path.Combine(versionPath, ".app-signed");
            // Always clear quarantine to avoid "damaged" warnings
            ClearMacQuarantine(appBundle);
            if (!IsMacAppSignatureCurrent(executable, signStampPath))
            {
                Logger.Info("Game", "Clearing macOS quarantine attributes...");
                MarkMacAppSigned(executable, signStampPath);
            }
            else
            {
                Logger.Info("Game", "Skipping app signing (already signed)");
            }
        }

        Logger.Info("Game", $"Launching: {executable}");
        Logger.Info("Game", $"Java: {javaPath}");
        Logger.Info("Game", $"AppDir: {gameDir}");
        Logger.Info("Game", $"UserData: {userDataDir}");

        // Build arguments matching old launcher
        var args = new List<string>
        {
            "--app-dir", gameDir,
            "--user-dir", userDataDir,
            "--java-exec", javaPath,
            "--auth-mode", "offline",
            "--uuid", uuid,
            "--name", _config.Nick
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Add environment variables
        startInfo.EnvironmentVariables["HYTALE_NICK"] = _config.Nick;
        
        // On Linux, set LD_LIBRARY_PATH to find native libraries (SDL3, etc.)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string clientDir = Path.Combine(versionPath, "Client");
            string existingLdPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
            string newLdPath = string.IsNullOrEmpty(existingLdPath) ? clientDir : $"{clientDir}:{existingLdPath}";
            startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = newLdPath;
            Logger.Info("Game", $"LD_LIBRARY_PATH set to: {newLdPath}");
        }

        // On macOS, set DYLD_LIBRARY_PATH so the client can load bundled libs
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string clientDir = Path.Combine(versionPath, "Client");
            string existingDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
            string newDyldPath = string.IsNullOrEmpty(existingDyldPath) ? clientDir : $"{clientDir}:{existingDyldPath}";
            startInfo.EnvironmentVariables["DYLD_LIBRARY_PATH"] = newDyldPath;
            Logger.Info("Game", $"DYLD_LIBRARY_PATH set to: {newDyldPath}");
        }
        
        _gameProcess = Process.Start(startInfo);
        
        if (_gameProcess != null)
        {
            Logger.Success("Game", $"Game started with PID: {_gameProcess.Id}");
            
            // Capture stdout and stderr for debugging
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await _gameProcess.StandardOutput.ReadLineAsync()) != null)
                    {
                        Logger.Info("GameOut", line);
                    }
                }
                catch { }
            });
            
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await _gameProcess.StandardError.ReadLineAsync()) != null)
                    {
                        Logger.Warning("GameErr", line);
                    }
                }
                catch { }
            });
            
            // Set Discord presence to Playing
            _discordService.SetPresence(DiscordService.PresenceState.Playing, $"Playing as {_config.Nick}");
            
            // Notify frontend that game has launched
            SendGameStateEvent("started");
            
            // Handle process exit in background
            _ = Task.Run(async () =>
            {
                await _gameProcess.WaitForExitAsync();
                Logger.Info("Game", $"Game process exited with code: {_gameProcess.ExitCode}");
                _gameProcess = null;
                
                // Set Discord presence back to Idle
                _discordService.SetPresence(DiscordService.PresenceState.Idle);
                
                // Notify frontend that game has exited
                SendGameStateEvent("stopped");
            });
        }
    }
    
    private void SendGameStateEvent(string state)
    {
        if (_mainWindow == null) return;
        
        try
        {
            var eventData = new
            {
                type = "event",
                eventName = "game-state",
                data = new { state }
            };
            _mainWindow.SendWebMessage(JsonSerializer.Serialize(eventData, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to send game state event: {ex.Message}");
        }
    }
    
    public void SetMainWindow(PhotinoWindow window)
    {
        _mainWindow = window;
    }

    private string GenerateOfflineUUID(string playerName)
    {
        // Generate UUID v3 from player name (same as old launcher)
        using var md5 = System.Security.Cryptography.MD5.Create();
        var inputBytes = System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName);
        var hashBytes = md5.ComputeHash(inputBytes);
        
        // Set version to 3 (MD5 based)
        hashBytes[6] = (byte)((hashBytes[6] & 0x0F) | 0x30);
        // Set variant
        hashBytes[8] = (byte)((hashBytes[8] & 0x3F) | 0x80);
        
        return new Guid(hashBytes).ToString();
    }

    public bool IsGameRunning()
    {
        return _gameProcess != null && !_gameProcess.HasExited;
    }

    public List<string> GetRecentLogs(int count = 10)
    {
        return Logger.GetRecentLogs(count);
    }

    public bool ExitGame()
    {
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            _gameProcess.Kill();
            _gameProcess = null;
            return true;
        }
        return false;
    }

    public bool DeleteGame(string branch, int versionNumber)
    {
        try
        {
            string normalizedBranch = NormalizeVersionType(branch);
            string versionPath = GetInstancePath(normalizedBranch, versionNumber);
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
            }
            if (versionNumber == 0)
            {
                var infoPath = GetLatestInfoPath(normalizedBranch);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game: {ex.Message}");
            return false;
        }
    }

    // Folder
    public bool OpenFolder()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{_appDir}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{_appDir}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start("xdg-open", $"\"{_appDir}\"");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string?> SelectInstanceDirectoryAsync()
    {
        // Folder picker is not available in Photino. Return the current path so
        // the frontend can show it and collect user input manually.
        return Task.FromResult<string?>(_config.InstanceDirectory);
    }

    // News - matches Go implementation
    public async Task<List<NewsItemResponse>> GetNewsAsync(int count)
    {
        try
        {
            string url = $"https://hytale.com/api/blog/post/published?limit={count}";
            var response = await HttpClient.GetStringAsync(url);
            var newsItems = JsonSerializer.Deserialize<List<HytaleNewsItem>>(response, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (newsItems == null) return new List<NewsItemResponse>();

            const string cdnUrl = "https://cdn.hytale.com/variants/blog_thumb_";
            var result = new List<NewsItemResponse>();

            foreach (var item in newsItems)
            {
                // Parse URL from publishedAt and slug
                var url2 = "";
                var date = "";
                if (DateTime.TryParse(item.PublishedAt, out var parsedDate))
                {
                    url2 = $"https://hytale.com/news/{parsedDate.Year}/{parsedDate.Month}/{item.Slug}";
                    date = parsedDate.ToString("MMMM dd, yyyy");
                }

                result.Add(new NewsItemResponse
                {
                    Title = item.Title ?? "",
                    Excerpt = HttpUtility.HtmlDecode(item.BodyExcerpt ?? ""),
                    Url = url2,
                    Date = date,
                    Author = item.Author ?? "Hytale Team",
                    ImageUrl = !string.IsNullOrEmpty(item.CoverImage?.S3Key) 
                        ? cdnUrl + item.CoverImage.S3Key 
                        : ""
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("News", $"Error fetching news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    // Update - download latest launcher per platform instead of in-place update
    public async Task<bool> UpdateAsync(JsonElement[]? args)
    {
        const string releasesPage = "https://github.com/yyyumeniku/HyPrism/releases/latest";

        try
        {
            // Fetch latest release assets
            var apiUrl = "https://api.github.com/repos/yyyumeniku/HyPrism/releases/latest";
            var json = await HttpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);
            var assets = doc.RootElement.GetProperty("assets");

            // Pick asset by platform/arch
            string? targetAsset = null;
            var arch = RuntimeInformation.ProcessArchitecture;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                targetAsset = "macos-arm64.dmg"; // Apple Silicon only
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                targetAsset = "windows-x64.zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                targetAsset = arch == Architecture.Arm64 ? "linux-arm64.tar.gz" : "linux-x64.AppImage";
            }

            if (string.IsNullOrWhiteSpace(targetAsset))
            {
                Logger.Warning("Update", "Unsupported OS for auto-download, opening releases page");
                BrowserOpenURL(releasesPage);
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
                BrowserOpenURL(releasesPage);
                return false;
            }

            var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloadsDir);
            var targetPath = Path.Combine(downloadsDir, assetName);

            Logger.Info("Update", $"Downloading latest launcher to {targetPath}");
            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
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

            // Platform-specific post-step
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var appPath = "/Applications/HyPrism.app";
                if (Directory.Exists(appPath))
                {
                    try
                    {
                        Logger.Warning("Update", "Removing existing /Applications/HyPrism.app");
                        Directory.Delete(appPath, recursive: true);
                    }
                    catch (Exception deleteEx)
                    {
                        Logger.Warning("Update", $"Failed to delete old app: {deleteEx.Message}");
                    }
                }

                try { Process.Start("open", targetPath); } catch (Exception openEx) { Logger.Warning("Update", $"Could not open DMG: {openEx.Message}"); }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { Process.Start("explorer.exe", $"/select,\"{targetPath}\""); } catch (Exception openEx) { Logger.Warning("Update", $"Could not open Explorer: {openEx.Message}"); }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try { Process.Start("xdg-open", targetPath); } catch (Exception openEx) { Logger.Warning("Update", $"Could not open file manager: {openEx.Message}"); }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Update failed: {ex.Message}");
            BrowserOpenURL(releasesPage);
            return false;
        }
    }

    // Browser
    public bool BrowserOpenURL(string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Music
    public bool GetMusicEnabled() => _config.MusicEnabled;
    
    public bool SetMusicEnabled(bool enabled)
    {
        _config.MusicEnabled = enabled;
        SaveConfig();
        return true;
    }

    // Online Mode removed: launcher always runs offline

    // CurseForge API constants
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int HytaleGameId = 70216; // Hytale game ID on CurseForge
    private const string CurseForgeApiKey = "$2a$10$bL4bIL5pUWqfcO7KQtnMReakwtfHbNKh6v1uTpKlzhwoueEJQnPnm";

    // Mod Manager with CurseForge API
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/mods/search?gameId={HytaleGameId}";
            
            if (!string.IsNullOrEmpty(query))
            {
                url += $"&searchFilter={Uri.EscapeDataString(query)}";
            }
            
            if (pageSize > 0)
            {
                url += $"&pageSize={pageSize}";
            }
            
            if (page > 0)
            {
                url += $"&index={page * pageSize}";
            }
            
            // Sort field: 1=Featured, 2=Popularity, 3=LastUpdated, 4=Name, 5=Author, 6=TotalDownloads
            if (sortField > 0)
            {
                url += $"&sortField={sortField}";
            }
            
            // Sort order: asc or desc
            if (sortOrder > 0)
            {
                url += $"&sortOrder={(sortOrder == 1 ? "asc" : "desc")}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (cfResponse?.Data == null)
            {
                return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
            }
            
            var mods = cfResponse.Data.Select(m => new ModInfo
            {
                Id = m.Id.ToString(),
                Name = m.Name ?? "",
                Slug = m.Slug ?? "",
                Summary = m.Summary ?? "",
                Description = m.Summary ?? "",
                Author = m.Authors?.FirstOrDefault()?.Name ?? "Unknown",
                DownloadCount = m.DownloadCount,
                IconUrl = m.Logo?.ThumbnailUrl ?? "",
                DateUpdated = m.DateModified ?? "",
                Categories = m.Categories?.Select(c => c.Name ?? "").ToList() ?? new List<string>(),
                LatestFileId = m.LatestFiles?.FirstOrDefault()?.Id.ToString() ?? "",
                Screenshots = m.Screenshots ?? new List<CurseForgeScreenshot>()
            }).ToList();
            
            return new ModSearchResult 
            { 
                Mods = mods, 
                TotalCount = cfResponse.Pagination?.TotalCount ?? mods.Count 
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Search failed: {ex.Message}");
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
        }
    }

    private async Task<CurseForgeMod?> GetCurseForgeModAsync(string modId)
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/mods/{modId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeModResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return cfResponse?.Data;
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to fetch mod metadata for {modId}: {ex.Message}");
            return null;
        }
    }

    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/mods/{modId}/files";
            if (pageSize > 0)
            {
                url += $"?pageSize={pageSize}";
                if (page > 0)
                {
                    url += $"&index={page * pageSize}";
                }
            }
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (cfResponse?.Data == null)
            {
                return new ModFilesResult { Files = new List<ModFileInfo>(), TotalCount = 0 };
            }
            
            var files = cfResponse.Data.Select(f => new ModFileInfo
            {
                Id = f.Id.ToString(),
                ModId = f.ModId.ToString(),
                DisplayName = f.DisplayName ?? "",
                FileName = f.FileName ?? "",
                FileLength = f.FileLength,
                DownloadUrl = f.DownloadUrl ?? "",
                FileDate = f.FileDate ?? "",
                ReleaseType = f.ReleaseType
            }).ToList();
            
            return new ModFilesResult 
            { 
                Files = files, 
                TotalCount = cfResponse.Pagination?.TotalCount ?? files.Count 
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to get mod files: {ex.Message}");
            return new ModFilesResult { Files = new List<ModFileInfo>(), TotalCount = 0 };
        }
    }

    private string GetModsPath(string versionPath)
    {
        var userDataPath = Path.Combine(versionPath, "UserData");
        var preferredPath = Path.Combine(userDataPath, "Mods");
        var legacyPath = Path.Combine(userDataPath, "mods");

        if (Directory.Exists(preferredPath))
        {
            return preferredPath;
        }

        if (Directory.Exists(legacyPath))
        {
            try
            {
                Directory.Move(legacyPath, preferredPath);
                return preferredPath;
            }
            catch
            {
                return legacyPath;
            }
        }

        Directory.CreateDirectory(preferredPath);
        return preferredPath;
    }

    // Photino bridge calls non-Async names; provide thin wrapper
    public Task<bool> InstallModFileToInstance(string modId, string fileId, string branch, int version)
    {
        return InstallModFileToInstanceAsync(modId, fileId, branch, version);
    }

    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/categories?gameId={HytaleGameId}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeCategoriesResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (cfResponse?.Data == null)
            {
                return new List<ModCategory>();
            }
            
            // Filter only root categories (classId == 0 or parentCategoryId == 0)
            return cfResponse.Data
                .Where(c => c.ParentCategoryId == 0 || c.IsClass == true)
                .Select(c => new ModCategory
                {
                    Id = c.Id,
                    Name = c.Name ?? ""
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to get categories: {ex.Message}");
            return new List<ModCategory>();
        }
    }

    public List<InstalledMod> GetInstanceInstalledMods(string branch, int version)
    {
        var result = new List<InstalledMod>();

        // Get current version's mods folder in UserData
        string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
        string versionPath = GetInstancePath(resolvedBranch, version);
        if (!Directory.Exists(versionPath)) return result;
        
        string modsPath = GetModsPath(versionPath);
        
        if (Directory.Exists(modsPath))
        {
            string manifestPath = Path.Combine(modsPath, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, JsonOptions) ?? new List<InstalledMod>();
                    // Normalize IDs and ensure CurseForgeId and screenshots are populated
                    foreach (var mod in mods)
                    {
                        if (!string.IsNullOrEmpty(mod.CurseForgeId) && !mod.Id.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
                        {
                            mod.Id = $"cf-{mod.CurseForgeId}";
                        }
                        else if (string.IsNullOrEmpty(mod.CurseForgeId) && mod.Id.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
                        {
                            mod.CurseForgeId = mod.Id.Replace("cf-", "");
                        }
                        mod.Screenshots ??= new List<CurseForgeScreenshot>();
                    }
                    return mods;
                }
                catch { }
            }
        }
        
        return result;
    }

    public async Task<bool> InstallModFileToInstanceAsync(string modId, string fileId, string branch, int version)
    {
        try
        {
            // Get the target instance path and create mods directory inside UserData
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = GetInstancePath(resolvedBranch, version);
            string modsPath = GetModsPath(versionPath);
            
            Logger.Info("Mods", $"Installing mod to: {modsPath}");
            
            // Get file info from CurseForge
            var url = $"{CurseForgeBaseUrl}/mods/{modId}/files/{fileId}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFileResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (cfResponse?.Data == null || string.IsNullOrEmpty(cfResponse.Data.DownloadUrl))
            {
                Logger.Error("Mods", "Could not get download URL for mod file");
                return false;
            }
            
            var fileInfo = cfResponse.Data;
            var fileName = fileInfo.FileName ?? $"mod_{modId}_{fileId}.jar";
            var filePath = Path.Combine(modsPath, fileName);

            // Fetch mod metadata to enrich manifest (icon, description, author)
            var modMeta = await GetCurseForgeModAsync(modId);
            string modName = modMeta?.Name ?? fileInfo.DisplayName ?? fileName;
            string modAuthor = modMeta?.Authors?.FirstOrDefault()?.Name ?? "Unknown";
            string modDescription = modMeta?.Summary ?? "";
            string iconUrl = modMeta?.Logo?.ThumbnailUrl ?? modMeta?.Logo?.Url ?? "";
            string modSlug = modMeta?.Slug ?? "";
            var screenshots = modMeta?.Screenshots ?? new List<CurseForgeScreenshot>();
            
            // Download the file
            Logger.Info("Mods", $"Downloading {fileName}...");
            using var downloadResponse = await HttpClient.GetAsync(fileInfo.DownloadUrl);
            downloadResponse.EnsureSuccessStatusCode();
            
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await downloadResponse.Content.CopyToAsync(fs);
            
            Logger.Success("Mods", $"Installed {fileName}");
            
            // Update manifest
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            var installedMods = new List<InstalledMod>();
            
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson, JsonOptions) ?? new List<InstalledMod>();
                }
                catch { }
            }
            
            // Remove existing entry for this mod if updating
            installedMods.RemoveAll(m => m.Id == $"cf-{modId}" || m.CurseForgeId == modId);
            
            // Add new entry
            installedMods.Add(new InstalledMod
            {
                Id = $"cf-{modId}",
                CurseForgeId = modId,
                FileId = fileId,
                Name = modName,
                FileName = fileName,
                Slug = modSlug,
                Enabled = true,
                Version = fileInfo.DisplayName ?? fileInfo.FileName ?? fileId,
                Author = modAuthor,
                Description = modDescription,
                IconUrl = iconUrl,
                FileDate = fileInfo.FileDate ?? "",
                Screenshots = screenshots
            });
            
            var manifestOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(installedMods, manifestOptions));
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to install mod: {ex.Message}");
            return false;
        }
    }

    public bool UninstallInstanceMod(string modId, string branch, int version)
    {
        try
        {
            // Get current version's mods folder in UserData
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = GetInstancePath(resolvedBranch, version);
            if (!Directory.Exists(versionPath)) return false;
            
            string modsPath = GetModsPath(versionPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            if (!File.Exists(manifestPath))
            {
                return false;
            }
            
            var manifestJson = File.ReadAllText(manifestPath);
            var installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson, JsonOptions) ?? new List<InstalledMod>();
            
            // Find the mod to uninstall
            var modToRemove = installedMods.FirstOrDefault(m => m.Id == modId || m.CurseForgeId == modId || m.Id == $"cf-{modId}");
            if (modToRemove == null)
            {
                return false;
            }
            
            // Delete the mod file
            if (!string.IsNullOrEmpty(modToRemove.FileName))
            {
                var filePath = Path.Combine(modsPath, modToRemove.FileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            
            // Remove from manifest
            installedMods.RemoveAll(m => m.Id == modId);
            var manifestOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(installedMods, manifestOptions));
            
            Logger.Success("Mods", $"Uninstalled mod: {modToRemove.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to uninstall mod: {ex.Message}");
            return false;
        }
    }

    public bool OpenInstanceModsFolder(string branch, int version)
    {
        try
        {
            // Get current version's mods folder in UserData
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = GetInstancePath(resolvedBranch, version);
            if (!Directory.Exists(versionPath)) Directory.CreateDirectory(versionPath);
            
            string modsPath = GetModsPath(versionPath);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{modsPath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{modsPath}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start("xdg-open", $"\"{modsPath}\"");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<List<ModUpdate>> CheckInstanceModUpdatesAsync(string branch, int version)
    {
        return Task.FromResult(new List<ModUpdate>());
    }

    public Task<List<ModUpdate>> CheckInstanceModUpdates(string branch, int version)
    {
        return CheckInstanceModUpdatesAsync(branch, version);
    }
}

// Models
public class Config
{
    public string Version { get; set; } = "2.0.0";
    public string UUID { get; set; } = "";
    public string Nick { get; set; } = "Hyprism";
    public string VersionType { get; set; } = "release";
    public int SelectedVersion { get; set; } = 0;
    public string InstanceDirectory { get; set; } = "";
    public bool MusicEnabled { get; set; } = true;
}

// News models matching Hytale API
public class HytaleNewsItem
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    
    [JsonPropertyName("bodyExcerpt")]
    public string? BodyExcerpt { get; set; }
    
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
    
    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; set; }
    
    [JsonPropertyName("coverImage")]
    public CoverImage? CoverImage { get; set; }
    
    [JsonPropertyName("author")]
    public string? Author { get; set; }
}

public class CoverImage
{
    [JsonPropertyName("s3Key")]
    public string? S3Key { get; set; }
}

// Response model for frontend
public class NewsItemResponse
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = "";
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
    
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
    
    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = "";
}

public class DownloadProgress
{
    public bool Success { get; set; }
    public int Progress { get; set; }
    public string? Error { get; set; }
}

public class ModSearchResult
{
    public List<ModInfo> Mods { get; set; } = new();
    public int TotalCount { get; set; }
}

public class ModInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public int DownloadCount { get; set; }
    public string IconUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public List<string> Categories { get; set; } = new();
    public string DateUpdated { get; set; } = "";
    public string LatestFileId { get; set; } = "";
    public List<CurseForgeScreenshot> Screenshots { get; set; } = new();
}

public class ModFilesResult
{
    public List<ModFileInfo> Files { get; set; } = new();
    public int TotalCount { get; set; }
}

public class ModFileInfo
{
    public string Id { get; set; } = "";
    public string ModId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long FileLength { get; set; }
    public string FileDate { get; set; } = "";
    public int ReleaseType { get; set; }
    public List<string> GameVersions { get; set; } = new();
    public int DownloadCount { get; set; }
}

public class ModCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class InstalledMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Version { get; set; } = "";
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string CurseForgeId { get; set; } = "";
    public string FileDate { get; set; } = "";
    public List<CurseForgeScreenshot> Screenshots { get; set; } = new();
}

public class ModUpdate
{
    public string ModId { get; set; } = "";
    public string CurrentFileId { get; set; } = "";
    public string LatestFileId { get; set; } = "";
    public string LatestFileName { get; set; } = "";
}

// CurseForge API response models
public class CurseForgeSearchResponse
{
    public List<CurseForgeMod>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgeModResponse
{
    public CurseForgeMod? Data { get; set; }
}

public class CurseForgePagination
{
    public int Index { get; set; }
    public int PageSize { get; set; }
    public int ResultCount { get; set; }
    public int TotalCount { get; set; }
}

public class CurseForgeMod
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Summary { get; set; }
    public int DownloadCount { get; set; }
    public string? DateCreated { get; set; }
    public string? DateModified { get; set; }
    public CurseForgeLogo? Logo { get; set; }
    public List<CurseForgeCategory>? Categories { get; set; }
    public List<CurseForgeAuthor>? Authors { get; set; }
    public List<CurseForgeFile>? LatestFiles { get; set; }
    public List<CurseForgeScreenshot>? Screenshots { get; set; }
}

public class CurseForgeScreenshot
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeLogo
{
    public int Id { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeCategory
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public int ParentCategoryId { get; set; }
    public bool? IsClass { get; set; }
}

public class CurseForgeAuthor
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public class CurseForgeFile
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? DisplayName { get; set; }
    public string? FileName { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileLength { get; set; }
    public string? FileDate { get; set; }
    public int ReleaseType { get; set; }
}

public class CurseForgeCategoriesResponse
{
    public List<CurseForgeCategory>? Data { get; set; }
}

public class CurseForgeFilesResponse
{
    public List<CurseForgeFile>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgeFileResponse
{
    public CurseForgeFile? Data { get; set; }
}
