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
    
    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);
    
    private static readonly HttpClient HttpClient = new()
    {
        // Use longer timeout for large file downloads - can be overridden per-request with cancellation tokens
        Timeout = TimeSpan.FromMinutes(30)
    };
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static AppService()
    {
        LoadEnvFile();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism/1.0");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public AppService()
    {
        _appDir = GetEffectiveAppDir();
        Directory.CreateDirectory(_appDir);
        _configPath = Path.Combine(_appDir, "config.json");
        _config = LoadConfig();
        
        // Update placeholder names to random ones immediately
        if (_config.Nick == "Hyprism" || _config.Nick == "HyPrism" || _config.Nick == "Player")
        {
            _config.Nick = GenerateRandomUsername();
            SaveConfig();
            Logger.Info("Config", $"Updated placeholder username to: {_config.Nick}");
        }
        
        MigrateLegacyData();
        _butlerService = new ButlerService(_appDir);
        _discordService = new DiscordService();
        _discordService.Initialize();
    }

    /// <summary>
    /// Gets the effective app directory, checking for environment variable override first.
    /// </summary>
    private string GetEffectiveAppDir()
    {
        // First check environment variable
        var envDir = Environment.GetEnvironmentVariable("HYPRISM_DATA");
        if (!string.IsNullOrWhiteSpace(envDir) && Directory.Exists(envDir))
        {
            return envDir;
        }

        // Then check if there's a config file in default location with custom path
        var defaultDir = GetDefaultAppDir();
        var defaultConfigPath = Path.Combine(defaultDir, "config.json");
        if (File.Exists(defaultConfigPath))
        {
            try
            {
                var configJson = File.ReadAllText(defaultConfigPath);
                var config = JsonSerializer.Deserialize<Config>(configJson, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.LauncherDataDirectory) && Directory.Exists(config.LauncherDataDirectory))
                {
                    return config.LauncherDataDirectory;
                }
            }
            catch { /* Ignore parsing errors, use default */ }
        }

        return defaultDir;
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
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism");
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

    /// <summary>
    /// Gets the UserData path for an instance. The game stores skins, settings, etc. here.
    /// </summary>
    private string GetInstanceUserDataPath(string versionPath)
    {
        return Path.Combine(versionPath, "UserData");
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
            ? Path.Combine(_appDir, "instances")
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

    private static void SafeCopyDirectory(string sourceDir, string destDir)
    {
        // CRITICAL: Prevent copying into itself (causes infinite loop)
        var normalizedSource = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar);
        
        if (normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase))
            return;
        if (normalizedDest.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        if (normalizedSource.StartsWith(normalizedDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
            
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile, overwrite: false);
            }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            SafeCopyDirectory(dir, destSubDir);
        }
    }

    private Config? LoadConfigFromPath(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private Config? LoadConfigFromToml(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var cfg = new Config();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                static string Unquote(string value)
                {
                    value = value.Trim();
                    // Handle double quotes
                    if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                    {
                        return value.Substring(1, value.Length - 2);
                    }
                    // Handle single quotes (TOML style)
                    if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 2)
                    {
                        return value.Substring(1, value.Length - 2);
                    }
                    return value;
                }

                var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim().ToLowerInvariant();
                var val = Unquote(parts[1]);

                switch (key)
                {
                    case "nick":
                    case "name":
                    case "username":
                        cfg.Nick = val;
                        break;
                    case "uuid":
                        cfg.UUID = val;
                        break;
                    case "instance_directory":
                    case "instancedirectory":
                    case "instance_dir":
                    case "instancepath":
                    case "instance_path":
                        cfg.InstanceDirectory = val;
                        break;
                    case "versiontype":
                    case "branch":
                        cfg.VersionType = NormalizeVersionType(val);
                        break;
                    case "selectedversion":
                        if (int.TryParse(val, out var sel)) cfg.SelectedVersion = sel;
                        break;
                }
            }
            return cfg;
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> GetLegacyRoots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            roots.Add(path);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Add(Path.Combine(appData, "hyprism"));
            Add(Path.Combine(appData, "Hyprism"));
            Add(Path.Combine(appData, "HyPrism")); // legacy casing
            Add(Path.Combine(appData, "HyPrismLauncher"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Add(Path.Combine(home, "Library", "Application Support", "hyprism"));
            Add(Path.Combine(home, "Library", "Application Support", "Hyprism"));
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                Add(Path.Combine(xdg, "hyprism"));
                Add(Path.Combine(xdg, "Hyprism"));
            }
            Add(Path.Combine(home, ".local", "share", "hyprism"));
            Add(Path.Combine(home, ".local", "share", "Hyprism"));
        }

        return roots;
    }

    private IEnumerable<string> GetInstanceRootsIncludingLegacy()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> YieldIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) yield break;
            if (!Directory.Exists(path)) yield break;

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
            {
                yield return full;
            }
        }

        foreach (var root in YieldIfExists(GetInstanceRoot()))
        {
            yield return root;
        }

        foreach (var legacy in GetLegacyRoots())
        {
            // Check legacy naming: 'instance' (singular) and 'instances' (plural)
            foreach (var r in YieldIfExists(Path.Combine(legacy, "instance")))
            {
                yield return r;
            }

            foreach (var r in YieldIfExists(Path.Combine(legacy, "instances")))
            {
                yield return r;
            }
        }

        // Also check old 'instance' folder in current app dir (singular -> plural migration)
        var oldInstanceDir = Path.Combine(_appDir, "instance");
        foreach (var r in YieldIfExists(oldInstanceDir))
        {
            yield return r;
        }
    }

    private string? FindExistingInstancePath(string branch, int version)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        string versionSegment = version == 0 ? "latest" : version.ToString();

        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            // New layout: branch/version
            var candidate1 = Path.Combine(root, normalizedBranch, versionSegment);
            if (Directory.Exists(candidate1))
            {
                return candidate1;
            }

            // Legacy dash layout: release-5
            var candidate2 = Path.Combine(root, $"{normalizedBranch}-{versionSegment}");
            if (Directory.Exists(candidate2))
            {
                return candidate2;
            }

            // Legacy dash layout with v prefix: release-v5
            var candidate3 = Path.Combine(root, $"{normalizedBranch}-v{versionSegment}");
            if (Directory.Exists(candidate3))
            {
                return candidate3;
            }
        }

        return null;
    }

    private string? LoadLegacyUuid(string legacyRoot)
    {
        var candidates = new[] { "uuid.txt", "uuid", "uuid.dat" };
        foreach (var name in candidates)
        {
            var path = Path.Combine(legacyRoot, name);
            if (!File.Exists(path)) continue;

            try
            {
                var content = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(content) && Guid.TryParse(content, out var guid))
                {
                    return guid.ToString();
                }
            }
            catch
            {
                // ignore malformed legacy uuid files
            }
        }

        return null;
    }

    private void MigrateLegacyData()
    {
        try
        {
            foreach (var legacyRoot in GetLegacyRoots())
            {
                if (!Directory.Exists(legacyRoot)) continue;

                Logger.Info("Migrate", $"Found legacy data at {legacyRoot}");

                var legacyConfigPath = Path.Combine(legacyRoot, "config.json");
                var legacyTomlPath = Path.Combine(legacyRoot, "config.toml");
                
                // Load both JSON and TOML configs
                var jsonConfig = LoadConfigFromPath(legacyConfigPath);
                var tomlConfig = LoadConfigFromToml(legacyTomlPath);
                
                // Prefer TOML if it has a custom nick (not default), or prefer whichever has custom data
                Config? legacyConfig = null;
                bool tomlHasCustomNick = tomlConfig != null && !string.IsNullOrWhiteSpace(tomlConfig.Nick) 
                    && !string.Equals(tomlConfig.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tomlConfig.Nick, "Player", StringComparison.OrdinalIgnoreCase);
                bool jsonHasCustomNick = jsonConfig != null && !string.IsNullOrWhiteSpace(jsonConfig.Nick)
                    && !string.Equals(jsonConfig.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(jsonConfig.Nick, "Player", StringComparison.OrdinalIgnoreCase);
                    
                if (tomlHasCustomNick)
                {
                    legacyConfig = tomlConfig;
                    Logger.Info("Migrate", $"Using legacy config.toml (has custom nick): nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (jsonHasCustomNick)
                {
                    legacyConfig = jsonConfig;
                    Logger.Info("Migrate", $"Using legacy config.json (has custom nick): nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (tomlConfig != null)
                {
                    legacyConfig = tomlConfig;
                    Logger.Info("Migrate", $"Using legacy config.toml: nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (jsonConfig != null)
                {
                    legacyConfig = jsonConfig;
                    Logger.Info("Migrate", $"Using legacy config.json: nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else
                {
                    Logger.Warning("Migrate", $"No valid config found in {legacyRoot}");
                }

                // Only merge legacy config when current user name is still a default/placeholder
                bool allowMerge = string.IsNullOrWhiteSpace(_config.Nick)
                                  || string.Equals(_config.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(_config.Nick, "Player", StringComparison.OrdinalIgnoreCase);

                if (!allowMerge)
                {
                    Logger.Info("Migrate", "Skipping legacy config merge because current nickname is custom.");
                }

                var updated = false;

                if (legacyConfig != null && allowMerge)
                {
                    Logger.Info("Migrate", $"Merging legacy config: nick={legacyConfig.Nick}");
                    if (!string.IsNullOrWhiteSpace(legacyConfig.Nick))
                    {
                        _config.Nick = legacyConfig.Nick;
                        updated = true;
                        Logger.Success("Migrate", $"Migrated nickname: {legacyConfig.Nick}");
                    }

                    if (string.IsNullOrWhiteSpace(_config.UUID) && !string.IsNullOrWhiteSpace(legacyConfig.UUID))
                    {
                        _config.UUID = legacyConfig.UUID;
                        updated = true;
                    }

                    if (string.IsNullOrWhiteSpace(_config.InstanceDirectory) && !string.IsNullOrWhiteSpace(legacyConfig.InstanceDirectory))
                    {
                        _config.InstanceDirectory = legacyConfig.InstanceDirectory;
                        updated = true;
                    }

                    if (_config.SelectedVersion == 0 && legacyConfig.SelectedVersion > 0)
                    {
                        _config.SelectedVersion = legacyConfig.SelectedVersion;
                        updated = true;
                    }

                    if (string.IsNullOrWhiteSpace(_config.VersionType) && !string.IsNullOrWhiteSpace(legacyConfig.VersionType))
                    {
                        _config.VersionType = NormalizeVersionType(legacyConfig.VersionType);
                        updated = true;
                    }
                }

                // Fallback: pick up a legacy uuid file if config lacked one
                if (string.IsNullOrWhiteSpace(_config.UUID))
                {
                    var legacyUuid = LoadLegacyUuid(legacyRoot);
                    if (!string.IsNullOrWhiteSpace(legacyUuid))
                    {
                        _config.UUID = legacyUuid;
                        updated = true;
                        Logger.Info("Migrate", "Recovered legacy UUID from legacy folder.");
                    }
                }

                if (updated)
                {
                    SaveConfigInternal(_config);
                    
                    // Delete old config.toml after successful migration
                    if (File.Exists(legacyTomlPath))
                    {
                        try
                        {
                            File.Delete(legacyTomlPath);
                            Logger.Success("Migrate", $"Deleted legacy config.toml at {legacyTomlPath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Migrate", $"Failed to delete legacy config.toml: {ex.Message}");
                        }
                    }
                }

                // Detect legacy instance folders and copy to new structure
                var legacyInstanceRoot = Path.Combine(legacyRoot, "instance");
                var legacyInstancesRoot = Path.Combine(legacyRoot, "instances"); // v1 naming
                if (!Directory.Exists(legacyInstanceRoot) && Directory.Exists(legacyInstancesRoot))
                {
                    legacyInstanceRoot = legacyInstancesRoot;
                }

                if (Directory.Exists(legacyInstanceRoot))
                {
                    Logger.Info("Migrate", $"Legacy instances detected at {legacyInstanceRoot}");
                    MigrateLegacyInstances(legacyInstanceRoot);
                }
            }

            // Also migrate old 'instance' folder in current app dir (singular -> plural)
            var oldInstanceDir = Path.Combine(_appDir, "instance");
            if (Directory.Exists(oldInstanceDir))
            {
                Logger.Info("Migrate", $"Old 'instance' folder detected at {oldInstanceDir}");
                MigrateLegacyInstances(oldInstanceDir);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Migrate", $"Legacy migration skipped: {ex.Message}");
        }
    }

    private void MigrateLegacyInstances(string legacyInstanceRoot)
    {
        try
        {
            var newInstanceRoot = GetInstanceRoot();
            
            // Check if source is the same as destination (case-insensitive for macOS)
            var normalizedSource = Path.GetFullPath(legacyInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedDest = Path.GetFullPath(newInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var isSameDirectory = normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase);
            
            // If same directory, we'll restructure in-place (rename release-v5 to release/5)
            // If different directories, we'll copy as before
            if (isSameDirectory)
            {
                Logger.Info("Migrate", "Source equals destination - will restructure legacy folders in-place");
                RestructureLegacyFoldersInPlace(legacyInstanceRoot);
                return;
            }
            
            // CRITICAL: Prevent migration if source is inside destination (would cause infinite loop)
            if (normalizedSource.StartsWith(normalizedDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("Migrate", "Skipping migration - source is inside destination");
                return;
            }
            
            Logger.Info("Migrate", $"Copying legacy instances from {legacyInstanceRoot} to {newInstanceRoot}");

            foreach (var legacyDir in Directory.GetDirectories(legacyInstanceRoot))
            {
                var folderName = Path.GetFileName(legacyDir);
                if (string.IsNullOrEmpty(folderName)) continue;
                
                // CRITICAL: Skip folders that are already branch names (new structure)
                // These indicate we're looking at already-migrated data
                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" || 
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already in new structure format");
                    continue;
                }

                // Parse legacy naming: "release-v5" or "release-5" or "release/5"
                string branch;
                string versionSegment;

                if (folderName.Contains("/"))
                {
                    // Already new format: release/5
                    var parts = folderName.Split('/');
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";
                }
                else if (folderName.Contains("-"))
                {
                    // Legacy dash format: release-v5 or release-5
                    var parts = folderName.Split('-', 2);
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";
                    
                    // Strip 'v' prefix if present
                    if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        versionSegment = versionSegment.Substring(1);
                    }
                }
                else
                {
                    // Unknown format - skip to be safe (could be new structure subfolder)
                    Logger.Info("Migrate", $"Skipping {folderName} - unknown format, may be new structure");
                    continue;
                }

                // Normalize branch name
                branch = NormalizeVersionType(branch);

                // Create target path in new structure: instance/release/5
                var targetBranch = Path.Combine(newInstanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);
                
                // CRITICAL: Ensure we're not copying a folder into itself
                var normalizedLegacy = Path.GetFullPath(legacyDir).TrimEnd(Path.DirectorySeparatorChar);
                var normalizedTarget = Path.GetFullPath(targetVersion).TrimEnd(Path.DirectorySeparatorChar);
                if (normalizedLegacy.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.StartsWith(normalizedLegacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedLegacy.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - would cause recursive copy");
                    continue;
                }

                // Skip if already exists in new location
                if (Directory.Exists(targetVersion) && IsClientPresent(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already exists at {targetVersion}");
                    continue;
                }

                Logger.Info("Migrate", $"Copying {folderName} -> {branch}/{versionSegment}");
                Directory.CreateDirectory(targetVersion);

                // Check if legacy has game/ subfolder or direct Client/ folder
                var legacyGameDir = Path.Combine(legacyDir, "game");
                var legacyClientDir = Path.Combine(legacyDir, "Client");
                
                if (Directory.Exists(legacyGameDir))
                {
                    // Legacy structure: release-v5/game/Client -> release/5/Client
                    foreach (var item in Directory.GetFileSystemEntries(legacyGameDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);
                        
                        if (Directory.Exists(item))
                        {
                            SafeCopyDirectory(item, dest);
                        }
                        else if (File.Exists(item))
                        {
                            File.Copy(item, dest, overwrite: false);
                        }
                    }
                    Logger.Success("Migrate", $"Migrated {folderName} (from game/ subfolder)");
                }
                else if (Directory.Exists(legacyClientDir))
                {
                    // Direct Client/ folder structure
                    foreach (var item in Directory.GetFileSystemEntries(legacyDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);
                        
                        if (Directory.Exists(item))
                        {
                            SafeCopyDirectory(item, dest);
                        }
                        else if (File.Exists(item))
                        {
                            File.Copy(item, dest, overwrite: false);
                        }
                    }
                    Logger.Success("Migrate", $"Migrated {folderName} (direct structure)");
                }
                else
                {
                    // Copy everything as-is
                    SafeCopyDirectory(legacyDir, targetVersion);
                    Logger.Success("Migrate", $"Migrated {folderName} (full copy)");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to migrate legacy instances: {ex.Message}");
        }
    }

    /// <summary>
    /// Restructure legacy folder format (release-v5) to new format (release/5) in-place.
    /// This is used when the instances folder is already in the correct location but has old naming.
    /// </summary>
    private void RestructureLegacyFoldersInPlace(string instanceRoot)
    {
        try
        {
            foreach (var legacyDir in Directory.GetDirectories(instanceRoot))
            {
                var folderName = Path.GetFileName(legacyDir);
                if (string.IsNullOrEmpty(folderName)) continue;
                
                // Skip folders that are already branch names (new structure)
                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" || 
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    // This is already new structure, skip
                    continue;
                }
                
                // Only process legacy dash format: release-v5 or release-5
                if (!folderName.Contains("-"))
                {
                    continue;
                }
                
                var parts = folderName.Split('-', 2);
                var branch = parts[0];
                var versionSegment = parts.Length > 1 ? parts[1] : "latest";
                
                // Strip 'v' prefix if present
                if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    versionSegment = versionSegment.Substring(1);
                }
                
                // Normalize branch name
                branch = NormalizeVersionType(branch);
                
                // Create target path in new structure: instances/release/5
                var targetBranch = Path.Combine(instanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);
                
                // Skip if target already exists
                if (Directory.Exists(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - target {branch}/{versionSegment} already exists");
                    continue;
                }
                
                Logger.Info("Migrate", $"Restructuring {folderName} -> {branch}/{versionSegment}");
                
                // Create the branch directory
                Directory.CreateDirectory(targetBranch);
                
                // Check if legacy has game/ subfolder - if so, move contents up
                var legacyGameDir = Path.Combine(legacyDir, "game");
                
                if (Directory.Exists(legacyGameDir))
                {
                    // Legacy structure: release-v5/game/Client -> release/5/Client
                    // Move the contents of game/ to the new version folder
                    Directory.CreateDirectory(targetVersion);
                    
                    foreach (var item in Directory.GetFileSystemEntries(legacyGameDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);
                        
                        if (Directory.Exists(item))
                        {
                            Directory.Move(item, dest);
                        }
                        else if (File.Exists(item))
                        {
                            File.Move(item, dest);
                        }
                    }
                    
                    // Clean up old structure
                    try
                    {
                        Directory.Delete(legacyDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("Migrate", $"Could not delete old folder {legacyDir}: {ex.Message}");
                    }
                    
                    Logger.Success("Migrate", $"Restructured {folderName} (from game/ subfolder)");
                }
                else
                {
                    // Direct structure - just rename the folder
                    try
                    {
                        Directory.Move(legacyDir, targetVersion);
                        Logger.Success("Migrate", $"Restructured {folderName} (direct rename)");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Migrate", $"Failed to rename {folderName}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to restructure legacy folders: {ex.Message}");
        }
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

    /// <summary>
    /// Gets the sequence of patch versions to apply for a differential update.
    /// Returns list of versions from (currentVersion + 1) to targetVersion inclusive.
    /// </summary>
    private static List<int> GetPatchSequence(int currentVersion, int targetVersion)
    {
        var patches = new List<int>();
        for (int v = currentVersion + 1; v <= targetVersion; v++)
        {
            patches.Add(v);
        }
        return patches;
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

    private string ResolveInstancePath(string branch, int version, bool preferExisting)
    {
        if (preferExisting)
        {
            var existing = FindExistingInstancePath(branch, version);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        return GetInstancePath(branch, version);
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

        // Default nick to random name if empty or placeholder
        if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player" || config.Nick == "Hyprism" || config.Nick == "HyPrism")
        {
            config.Nick = GenerateRandomUsername();
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
    
    /// <summary>
    /// Generates a random username for new users.
    /// Format: Adjective + Noun + 4-digit number (max 16 chars total)
    /// </summary>
    private static string GenerateRandomUsername()
    {
        var random = new Random();
        
        // Short adjectives (max 5 chars)
        var adjectives = new[] { 
            "Happy", "Swift", "Brave", "Noble", "Quiet", "Bold", "Lucky", "Epic",
            "Jolly", "Lunar", "Solar", "Azure", "Royal", "Foxy", "Wacky", "Zesty",
            "Fizzy", "Dizzy", "Funky", "Jazzy", "Snowy", "Rainy", "Sunny", "Windy"
        };
        
        // Short nouns (max 6 chars)
        var nouns = new[] {
            "Panda", "Tiger", "Wolf", "Dragon", "Knight", "Ranger", "Mage", "Fox",
            "Bear", "Eagle", "Hawk", "Lion", "Falcon", "Raven", "Owl", "Shark",
            "Cobra", "Viper", "Lynx", "Badger", "Otter", "Pirate", "Ninja", "Viking"
        };
        
        var adj = adjectives[random.Next(adjectives.Length)];
        var noun = nouns[random.Next(nouns.Length)];
        var num = random.Next(1000, 9999);
        
        var name = $"{adj}{noun}{num}";
        // Safety truncate to 16 chars
        return name.Length <= 16 ? name : name.Substring(0, 16);
    }

    // Config
    public Config QueryConfig() => _config;

    public string GetNick() => _config.Nick;
    
    public string GetUUID() => _config.UUID;
    
    /// <summary>
    /// Gets the avatar preview image as base64 data URL for displaying in the launcher.
    /// Returns null if no avatar preview exists.
    /// </summary>
    public string? GetAvatarPreview()
    {
        return GetAvatarPreviewForUUID(_config.UUID);
    }
    
    /// <summary>
    /// Gets the avatar preview for a specific UUID.
    /// Checks profile folder first, then game cache, then persistent backup.
    /// </summary>
    public string? GetAvatarPreviewForUUID(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid))
            {
                return null;
            }
            
            // First check profile folder (most reliable for stored profiles)
            var profile = _config.Profiles?.FirstOrDefault(p => p.UUID == uuid);
            if (profile != null)
            {
                var profilesDir = GetProfilesFolder();
                var safeName = SanitizeFileName(profile.Name);
                var profileDir = Path.Combine(profilesDir, safeName);
                var profileAvatarPath = Path.Combine(profileDir, "avatar.png");
                
                if (File.Exists(profileAvatarPath) && new FileInfo(profileAvatarPath).Length > 100)
                {
                    var bytes = File.ReadAllBytes(profileAvatarPath);
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
            }
            
            var branch = NormalizeVersionType(_config.VersionType);
            var versionPath = ResolveInstancePath(branch, 0, preferExisting: true);
            var userDataPath = GetInstanceUserDataPath(versionPath);
            var cacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var cachePath = Path.Combine(cacheDir, $"{uuid}.png");
            var persistentPath = Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
            
            // Check what files exist
            var cacheExists = File.Exists(cachePath) && new FileInfo(cachePath).Length > 100;
            var persistentExists = File.Exists(persistentPath) && new FileInfo(persistentPath).Length > 100;
            
            // Use cache if available (prefer newer file)
            if (cacheExists)
            {
                var cacheInfo = new FileInfo(cachePath);
                var persistentInfo = persistentExists ? new FileInfo(persistentPath) : null;
                var useCache = persistentInfo == null || cacheInfo.LastWriteTimeUtc > persistentInfo.LastWriteTimeUtc;
                
                if (useCache)
                {
                    var bytes = File.ReadAllBytes(cachePath);
                    // Backup to persistent storage
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(persistentPath)!);
                        File.Copy(cachePath, persistentPath, true);
                    }
                    catch { }
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
            }
            
            // Fall back to persistent backup
            if (persistentExists)
            {
                var bytes = File.ReadAllBytes(persistentPath);
                return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Avatar", $"Could not load avatar preview: {ex.Message}");
            return null;
        }
    }

    public string GetCustomInstanceDir() => _config.InstanceDirectory ?? "";

    public bool SetUUID(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return false;
        if (!Guid.TryParse(uuid.Trim(), out var parsed)) return false;
        _config.UUID = parsed.ToString();
        SaveConfig();
        return true;
    }
    
    /// <summary>
    /// Clears the avatar cache for the current UUID.
    /// Call this when the user wants to reset their avatar.
    /// </summary>
    public bool ClearAvatarCache()
    {
        try
        {
            var uuid = _config.UUID;
            if (string.IsNullOrWhiteSpace(uuid)) return false;
            
            // Clear persistent backup
            var persistentPath = Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
            if (File.Exists(persistentPath))
            {
                File.Delete(persistentPath);
                Logger.Info("Avatar", $"Deleted persistent avatar for {uuid}");
            }
            
            // Clear game cache for all instances
            var instanceRoot = GetInstanceRoot();
            if (Directory.Exists(instanceRoot))
            {
                foreach (var branchDir in Directory.GetDirectories(instanceRoot))
                {
                    foreach (var versionDir in Directory.GetDirectories(branchDir))
                    {
                        var avatarPath = Path.Combine(versionDir, "UserData", "CachedAvatarPreviews", $"{uuid}.png");
                        if (File.Exists(avatarPath))
                        {
                            File.Delete(avatarPath);
                            Logger.Info("Avatar", $"Deleted cached avatar at {avatarPath}");
                        }
                    }
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Avatar", $"Failed to clear avatar cache: {ex.Message}");
            return false;
        }
    }
    
    public bool SetNick(string nick)
    {
        // Validate nickname length (1-16 characters)
        var trimmed = nick?.Trim() ?? "";
        if (trimmed.Length < 1 || trimmed.Length > 16)
        {
            Logger.Warning("Config", $"Invalid nickname length: {trimmed.Length} (must be 1-16 chars)");
            return false;
        }
        _config.Nick = trimmed;
        SaveConfig();
        return true;
    }
    
    // ========== Profile Management ==========
    
    /// <summary>
    /// Gets all saved profiles, filtering out any with null/empty names.
    /// </summary>
    public List<Profile> GetProfiles()
    {
        // Clean up any null/empty profiles first
        if (_config.Profiles != null)
        {
            var validProfiles = _config.Profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.UUID))
                .ToList();
            
            if (validProfiles.Count != _config.Profiles.Count)
            {
                Logger.Info("Profile", $"Cleaned up {_config.Profiles.Count - validProfiles.Count} invalid profiles");
                _config.Profiles = validProfiles;
                SaveConfig();
            }
        }
        
        var profiles = _config.Profiles ?? new List<Profile>();
        Logger.Info("Profile", $"GetProfiles returning {profiles.Count} profiles");
        return profiles;
    }
    
    /// <summary>
    /// Gets the currently active profile index. -1 means no profile selected.
    /// </summary>
    public int GetActiveProfileIndex()
    {
        return _config.ActiveProfileIndex;
    }
    
    /// <summary>
    /// Creates a new profile with the given name and UUID.
    /// Returns the created profile.
    /// </summary>
    public Profile? CreateProfile(string name, string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
            {
                Logger.Warning("Profile", $"Cannot create profile with empty name or UUID");
                return null;
            }
            
            // Validate name length (1-16 characters)
            var trimmedName = name.Trim();
            if (trimmedName.Length < 1 || trimmedName.Length > 16)
            {
                Logger.Warning("Profile", $"Invalid name length: {trimmedName.Length} (must be 1-16 chars)");
                return null;
            }
            
            // Validate UUID format
            if (!Guid.TryParse(uuid.Trim(), out var parsedUuid))
            {
                Logger.Warning("Profile", $"Invalid UUID format: {uuid}");
                return null;
            }
            
            var profile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = parsedUuid.ToString(),
                Name = trimmedName,
                CreatedAt = DateTime.UtcNow
            };
            
            _config.Profiles ??= new List<Profile>();
            _config.Profiles.Add(profile);
            Logger.Info("Profile", $"Profile added to list. Total profiles: {_config.Profiles.Count}");
            SaveConfig();
            Logger.Info("Profile", $"Config saved to disk");
            
            // Save profile to disk folder
            SaveProfileToDisk(profile);
            
            Logger.Success("Profile", $"Created profile '{trimmedName}' with UUID {parsedUuid}");
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to create profile: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Deletes a profile by its ID.
    /// Returns true if successful.
    /// </summary>
    public bool DeleteProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }
            
            var index = _config.Profiles?.FindIndex(p => p.Id == profileId) ?? -1;
            if (index < 0)
            {
                return false;
            }
            
            var profile = _config.Profiles![index];
            _config.Profiles.RemoveAt(index);
            
            // Adjust active profile index if needed
            if (_config.ActiveProfileIndex == index)
            {
                _config.ActiveProfileIndex = -1;
            }
            else if (_config.ActiveProfileIndex > index)
            {
                _config.ActiveProfileIndex--;
            }
            
            SaveConfig();
            
            // Delete profile folder from disk (pass name for name-based folder)
            DeleteProfileFromDisk(profileId, profile.Name);
            
            Logger.Success("Profile", $"Deleted profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to delete profile: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Switches to a profile by its index.
    /// Returns true if successful.
    /// </summary>
    public bool SwitchProfile(int index)
    {
        try
        {
            if (_config.Profiles == null || index < 0 || index >= _config.Profiles.Count)
            {
                return false;
            }
            
            // First, backup current profile's skin data before switching
            var currentUuid = _config.UUID;
            if (!string.IsNullOrWhiteSpace(currentUuid))
            {
                BackupProfileSkinData(currentUuid);
            }
            
            var profile = _config.Profiles[index];
            
            // Restore the new profile's skin data
            RestoreProfileSkinData(profile);
            
            // Update current UUID and Nick
            _config.UUID = profile.UUID;
            _config.Nick = profile.Name;
            _config.ActiveProfileIndex = index;
            SaveConfig();
            
            Logger.Success("Profile", $"Switched to profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to switch profile: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Updates an existing profile.
    /// </summary>
    public bool UpdateProfile(string profileId, string? newName, string? newUuid)
    {
        try
        {
            var profile = _config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return false;
            }
            
            if (!string.IsNullOrWhiteSpace(newName))
            {
                profile.Name = newName.Trim();
            }
            
            if (!string.IsNullOrWhiteSpace(newUuid) && Guid.TryParse(newUuid.Trim(), out var parsedUuid))
            {
                profile.UUID = parsedUuid.ToString();
            }
            
            // If this is the active profile, also update current UUID/Nick
            var index = _config.Profiles!.FindIndex(p => p.Id == profileId);
            if (index == _config.ActiveProfileIndex)
            {
                _config.UUID = profile.UUID;
                _config.Nick = profile.Name;
            }
            
            SaveConfig();
            
            // Update profile on disk
            UpdateProfileOnDisk(profile);
            
            Logger.Success("Profile", $"Updated profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to update profile: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Gets the path to the Profiles folder.
    /// </summary>
    private string GetProfilesFolder()
    {
        var profilesDir = Path.Combine(_appDir, "Profiles");
        Directory.CreateDirectory(profilesDir);
        return profilesDir;
    }
    
    /// <summary>
    /// Saves a profile to disk as a .sh file with name and UUID, plus avatar if available.
    /// </summary>
    private void SaveProfileToDisk(Profile profile)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            // Use profile name as folder name (sanitize for filesystem)
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            Directory.CreateDirectory(profileDir);
            
            // Create the shell script with profile info
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            // Copy skin and avatar from game cache to profile folder
            CopyProfileSkinData(profile.UUID, profileDir);
            
            Logger.Info("Profile", $"Saved profile to disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save profile to disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Copies skin and avatar data from game cache to a profile folder.
    /// </summary>
    private void CopyProfileSkinData(string uuid, string profileDir)
    {
        try
        {
            // Get game UserData path
            var branch = NormalizeVersionType(_config.VersionType);
            var versionPath = ResolveInstancePath(branch, 0, preferExisting: true);
            var userDataPath = GetInstanceUserDataPath(versionPath);
            
            // Copy skin JSON
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = Path.Combine(profileDir, "skin.json");
                File.Copy(skinPath, destPath, true);
                Logger.Info("Profile", $"Copied skin for UUID {uuid}");
            }
            
            // Copy avatar PNG
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var avatarPath = Path.Combine(avatarCacheDir, $"{uuid}.png");
            if (File.Exists(avatarPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(avatarPath, destPath, true);
                Logger.Info("Profile", $"Copied avatar for UUID {uuid}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to copy skin data: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Copies the avatar for a given UUID to a profile folder (legacy, now uses CopyProfileSkinData).
    /// </summary>
    private void CopyProfileAvatar(string uuid, string profileDir)
    {
        try
        {
            // Check persistent backup first
            var persistentPath = Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
            if (File.Exists(persistentPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(persistentPath, destPath, true);
                Logger.Info("Profile", $"Copied avatar from backup for {uuid}");
                return;
            }
            
            // Check game cache
            var branch = NormalizeVersionType(_config.VersionType);
            var versionPath = ResolveInstancePath(branch, 0, preferExisting: true);
            var userDataPath = GetInstanceUserDataPath(versionPath);
            var cacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var avatarPath = Path.Combine(cacheDir, $"{uuid}.png");
            
            if (File.Exists(avatarPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(avatarPath, destPath, true);
                Logger.Info("Profile", $"Copied avatar from cache for {uuid}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to copy avatar: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Updates a profile's disk files when it's modified.
    /// </summary>
    private void UpdateProfileOnDisk(Profile profile)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            
            // Also check for old folder with ID and rename it
            var oldProfileDir = Path.Combine(profilesDir, profile.Id);
            if (Directory.Exists(oldProfileDir) && !Directory.Exists(profileDir))
            {
                Directory.Move(oldProfileDir, profileDir);
            }
            
            if (!Directory.Exists(profileDir))
            {
                SaveProfileToDisk(profile);
                return;
            }
            
            // Remove old .sh files
            foreach (var oldSh in Directory.GetFiles(profileDir, "*.sh"))
            {
                File.Delete(oldSh);
            }
            
            // Create new .sh file
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}
# Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            // Update avatar
            CopyProfileAvatar(profile.UUID, profileDir);
            
            Logger.Info("Profile", $"Updated profile on disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to update profile on disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Deletes a profile's disk folder.
    /// </summary>
    private void DeleteProfileFromDisk(string profileId, string? profileName = null)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            
            // Try to delete by name first if provided
            if (!string.IsNullOrEmpty(profileName))
            {
                var safeName = SanitizeFileName(profileName);
                var profileDirByName = Path.Combine(profilesDir, safeName);
                if (Directory.Exists(profileDirByName))
                {
                    Directory.Delete(profileDirByName, true);
                    Logger.Info("Profile", $"Deleted profile from disk: {profileDirByName}");
                    return;
                }
            }
            
            // Fallback to ID-based folder (for migration)
            var profileDir = Path.Combine(profilesDir, profileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
                Logger.Info("Profile", $"Deleted profile from disk: {profileDir}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to delete profile from disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sanitizes a string to be safe for use as a filename.
    /// </summary>
    private string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
    }
    
    /// <summary>
    /// Backs up the skin data for a profile (from game cache to profile folder).
    /// </summary>
    private void BackupProfileSkinData(string uuid)
    {
        try
        {
            // Find the profile by UUID
            var profile = _config.Profiles?.FirstOrDefault(p => p.UUID == uuid);
            if (profile == null)
            {
                return;
            }
            
            var profilesDir = GetProfilesFolder();
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            Directory.CreateDirectory(profileDir);
            
            // Get game UserData path
            var branch = NormalizeVersionType(_config.VersionType);
            var versionPath = ResolveInstancePath(branch, 0, preferExisting: true);
            var userDataPath = GetInstanceUserDataPath(versionPath);
            
            // Backup skin JSON
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = Path.Combine(profileDir, "skin.json");
                var skinJson = File.ReadAllText(skinPath);
                File.Copy(skinPath, destPath, true);
                Logger.Info("Profile", $"Backed up skin for {profile.Name} ({skinJson.Length} bytes)");
            }
            else
            {
                Logger.Warning("Profile", $"No skin file found to backup for {profile.Name} at {skinPath}");
            }
            
            // Backup avatar preview
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var avatarPath = Path.Combine(avatarCacheDir, $"{uuid}.png");
            if (File.Exists(avatarPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(avatarPath, destPath, true);
                Logger.Info("Profile", $"Backed up avatar for {profile.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to backup skin data: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Restores the skin data for a profile (from profile folder to game cache).
    /// </summary>
    private void RestoreProfileSkinData(Profile profile)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            
            if (!Directory.Exists(profileDir))
            {
                Logger.Info("Profile", $"No profile folder to restore from for {profile.Name}");
                return;
            }
            
            // Get game UserData path
            var branch = NormalizeVersionType(_config.VersionType);
            var versionPath = ResolveInstancePath(branch, 0, preferExisting: true);
            var userDataPath = GetInstanceUserDataPath(versionPath);
            
            // Restore skin JSON
            var skinBackupPath = Path.Combine(profileDir, "skin.json");
            if (File.Exists(skinBackupPath))
            {
                var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
                Directory.CreateDirectory(skinCacheDir);
                var destPath = Path.Combine(skinCacheDir, $"{profile.UUID}.json");
                File.Copy(skinBackupPath, destPath, true);
                Logger.Success("Profile", $"Restored skin for {profile.Name}");
                
                // Also push the complete skin data to the auth server to ensure it's in sync
                // This is crucial because the auth server MERGES updates, so if it has stale data,
                // it will corrupt the skin. By sending the full skin.json, we ensure a fresh start.
                // Note: This happens in the background and doesn't block profile switching
                if (_config.OnlineMode && !string.IsNullOrWhiteSpace(_config.AuthDomain))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Read the complete skin JSON from the backup
                            var skinJson = await File.ReadAllTextAsync(skinBackupPath);
                            
                            using var httpClient = new HttpClient();
                            httpClient.Timeout = TimeSpan.FromSeconds(10);
                            
                            var content = new StringContent(skinJson, System.Text.Encoding.UTF8, "application/json");
                            
                            // Use the account-data endpoint which accepts skin updates
                            // The UUID in the path tells the server which profile to update
                            var response = await httpClient.PutAsync(
                                $"https://{_config.AuthDomain}/account-data/skin/{profile.UUID}",
                                content
                            );
                            
                            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
                            {
                                Logger.Success("Profile", $"Synced complete skin to auth server for {profile.Name}");
                            }
                            else
                            {
                                Logger.Warning("Profile", $"Failed to sync skin to auth server: {response.StatusCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Profile", $"Failed to sync skin to auth server: {ex.Message}");
                        }
                    });
                }
            }
            
            // Restore avatar preview
            var avatarBackupPath = Path.Combine(profileDir, "avatar.png");
            if (File.Exists(avatarBackupPath))
            {
                var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
                Directory.CreateDirectory(avatarCacheDir);
                var destPath = Path.Combine(avatarCacheDir, $"{profile.UUID}.png");
                File.Copy(avatarBackupPath, destPath, true);
                Logger.Success("Profile", $"Restored avatar for {profile.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to restore skin data: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Saves the current UUID/Nick as a new profile.
    /// Returns the created profile.
    /// </summary>
    public Profile? SaveCurrentAsProfile()
    {
        var uuid = _config.UUID;
        var name = _config.Nick;
        
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        
        // Check if a profile with this UUID already exists
        var existing = _config.Profiles?.FirstOrDefault(p => p.UUID == uuid);
        if (existing != null)
        {
            // Update existing profile
            existing.Name = name;
            SaveConfig();
            UpdateProfileOnDisk(existing);
            return existing;
        }
        
        // Create new profile
        return CreateProfile(name, uuid);
    }

    public Task<string?> SetInstanceDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom instance directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _config.InstanceDirectory = null!;
                SaveConfig();
                Logger.Success("Config", "Instance directory cleared, using default");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(Path.Combine(_appDir, expanded));
            }

            Directory.CreateDirectory(expanded);

            _config.InstanceDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Instance directory set to {expanded}");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    public string GetLauncherVersion()
    {
        // Unified launcher version for all branches
        return "2.0.3";
    }

    /// <summary>
    /// Check if Rosetta 2 is installed on macOS Apple Silicon.
    /// Returns null if not on macOS or if Rosetta is installed.
    /// Returns a warning object if Rosetta is needed but not installed.
    /// </summary>
    public RosettaStatus? CheckRosettaStatus()
    {
        // Only relevant on macOS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        // Only relevant on Apple Silicon (ARM64)
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            return null;
        }

        try
        {
            // Check if Rosetta is installed by checking for the runtime at /Library/Apple/usr/share/rosetta
            var rosettaPath = "/Library/Apple/usr/share/rosetta";
            if (Directory.Exists(rosettaPath))
            {
                Logger.Info("Rosetta", "Rosetta 2 is installed");
                return null; // Rosetta is installed, no warning needed
            }

            // Also try running arch -x86_64 to verify
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/arch",
                    Arguments = "-x86_64 /usr/bin/true",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
                if (process?.ExitCode == 0)
                {
                    Logger.Info("Rosetta", "Rosetta 2 is installed (verified via arch command)");
                    return null;
                }
            }
            catch
            {
                // Ignore, proceed with warning
            }

            Logger.Warning("Rosetta", "Rosetta 2 is NOT installed - Hytale requires it to run on Apple Silicon");
            return new RosettaStatus
            {
                NeedsInstall = true,
                Message = "Rosetta 2 is required to run Hytale on Apple Silicon Macs.",
                Command = "softwareupdate --install-rosetta --agree-to-license",
                TutorialUrl = "https://www.youtube.com/watch?v=1W2vuSfnpXw"
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Rosetta", $"Failed to check Rosetta status: {ex.Message}");
            return null;
        }
    }

    // Version Management
    public string GetVersionType() => _config.VersionType;
    
    public bool SetVersionType(string versionType)
    {
        _config.VersionType = NormalizeVersionType(versionType);
        SaveConfig();
        return true;
    }

    // Returns list of available version numbers by checking Hytale's patch server
    // Uses caching to start from the last known version instead of version 1
    public async Task<List<int>> GetVersionListAsync(string branch)
    {
        var normalizedBranch = NormalizeVersionType(branch);

        var result = new List<int>();
        string osName = GetOS();
        string arch = GetArch();
        string apiVersionType = normalizedBranch;

        // Load version cache
        var cache = LoadVersionCache();
        int startVersion = 1;
        
        // If we have cached versions for this branch, start from the highest known version + 1
        if (cache.KnownVersions.TryGetValue(normalizedBranch, out var knownVersions) && knownVersions.Count > 0)
        {
            startVersion = knownVersions.Max() + 1; // Start checking from the NEXT version after the highest known
            // Add all known versions to result first
            result.AddRange(knownVersions);
        }

        // Check for new versions starting from startVersion
        int currentVersion = startVersion;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 3; // Stop after 3 consecutive non-existent versions

        while (consecutiveFailures < maxConsecutiveFailures)
        {
            var (version, exists) = await CheckVersionExistsAsync(osName, arch, apiVersionType, currentVersion);
            
            if (exists)
            {
                if (!result.Contains(version))
                {
                    result.Add(version);
                }
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
            }
            
            currentVersion++;
        }

        // Also verify that all cached versions still exist (in parallel)
        if (knownVersions != null && knownVersions.Count > 0)
        {
            var verifyTasks = knownVersions
                .Where(v => v < startVersion)
                .Select(v => CheckVersionExistsAsync(osName, arch, apiVersionType, v))
                .ToList();
            
            if (verifyTasks.Count > 0)
            {
                var verifyResults = await Task.WhenAll(verifyTasks);
                foreach (var (version, exists) in verifyResults)
                {
                    if (exists && !result.Contains(version))
                    {
                        result.Add(version);
                    }
                }
            }
        }

        result.Sort((a, b) => b.CompareTo(a)); // Sort descending (latest first)
        
        // Save updated cache
        cache.KnownVersions[normalizedBranch] = result;
        cache.LastUpdated = DateTime.UtcNow;
        SaveVersionCache(cache);
        
        Logger.Info("Version", $"Found {result.Count} versions for {branch}: [{string.Join(", ", result)}]");
        return result;
    }

    private string GetVersionCachePath()
    {
        return Path.Combine(_appDir, "version_cache.json");
    }

    private VersionCache LoadVersionCache()
    {
        try
        {
            var path = GetVersionCachePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cache = JsonSerializer.Deserialize<VersionCache>(json);
                if (cache != null)
                {
                    return cache;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to load version cache: {ex.Message}");
        }
        return new VersionCache();
    }

    private void SaveVersionCache(VersionCache cache)
    {
        try
        {
            var path = GetVersionCachePath();
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to save version cache: {ex.Message}");
        }
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
            var resolvedLatest = ResolveInstancePath(normalizedBranch, 0, preferExisting: true);
            bool hasClient = IsClientPresent(resolvedLatest);
            Logger.Info("Version", $"IsVersionInstalled check for version 0 (latest): path={resolvedLatest}, hasClient={hasClient}");
            return hasClient;
        }
        
        string versionPath = ResolveInstancePath(normalizedBranch, versionNumber, preferExisting: true);

        if (!IsClientPresent(versionPath))
        {
            // Last chance: try legacy dash naming in legacy roots
            var legacy = FindExistingInstancePath(normalizedBranch, versionNumber);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                Logger.Info("Version", $"IsVersionInstalled: found legacy layout at {legacy}");
                return IsClientPresent(legacy);
            }
            return false;
        }

        return true;
    }

    private bool IsClientPresent(string versionPath)
    {
        // Try multiple layouts: new layout (Client/...) and legacy layout (game/Client/...)
        var subfolders = new[] { "", "game" };

        foreach (var sub in subfolders)
        {
            string basePath = string.IsNullOrEmpty(sub) ? versionPath : Path.Combine(versionPath, sub);
            string clientPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                clientPath = Path.Combine(basePath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                clientPath = Path.Combine(basePath, "Client", "HytaleClient.exe");
            }
            else
            {
                clientPath = Path.Combine(basePath, "Client", "HytaleClient");
            }

            if (File.Exists(clientPath))
            {
                Logger.Info("Version", $"IsClientPresent: found at {clientPath}");
                return true;
            }
        }

        Logger.Info("Version", $"IsClientPresent: not found in {versionPath}");
        return false;
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

    /// <summary>
    /// Checks if Assets.zip exists for the specified branch and version.
    /// Assets.zip is required for the skin customizer to work.
    /// </summary>
    public bool HasAssetsZip(string branch, int version)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
        return HasAssetsZipInternal(versionPath);
    }
    
    /// <summary>
    /// Gets the path to Assets.zip if it exists, or null if not found.
    /// </summary>
    public string? GetAssetsZipPath(string branch, int version)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
        var assetsZipPath = GetAssetsZipPathInternal(versionPath);
        return File.Exists(assetsZipPath) ? assetsZipPath : null;
    }
    
    private bool HasAssetsZipInternal(string versionPath)
    {
        var assetsZipPath = GetAssetsZipPathInternal(versionPath);
        bool exists = File.Exists(assetsZipPath);
        Logger.Info("Assets", $"HasAssetsZip: path={assetsZipPath}, exists={exists}");
        return exists;
    }
    
    private string GetAssetsZipPathInternal(string versionPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets.zip");
        }
        else
        {
            return Path.Combine(versionPath, "Client", "Assets.zip");
        }
    }
    
    // Cosmetic category file mappings (matching auth server structure)
    private static readonly Dictionary<string, string> CosmeticCategoryMap = new()
    {
        { "BodyCharacteristics.json", "bodyCharacteristic" },
        { "Capes.json", "cape" },
        { "EarAccessory.json", "earAccessory" },
        { "Ears.json", "ears" },
        { "Eyebrows.json", "eyebrows" },
        { "Eyes.json", "eyes" },
        { "Faces.json", "face" },
        { "FaceAccessory.json", "faceAccessory" },
        { "FacialHair.json", "facialHair" },
        { "Gloves.json", "gloves" },
        { "Haircuts.json", "haircut" },
        { "HeadAccessory.json", "headAccessory" },
        { "Mouths.json", "mouth" },
        { "Overpants.json", "overpants" },
        { "Overtops.json", "overtop" },
        { "Pants.json", "pants" },
        { "Shoes.json", "shoes" },
        { "SkinFeatures.json", "skinFeature" },
        { "Undertops.json", "undertop" },
        { "Underwear.json", "underwear" }
    };
    
    /// <summary>
    /// Gets the available cosmetics from the Assets.zip file for the specified instance.
    /// Returns a dictionary where keys are category names and values are lists of cosmetic IDs.
    /// </summary>
    public Dictionary<string, List<string>>? GetCosmeticsList(string branch, int version)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);
            var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
            var assetsZipPath = GetAssetsZipPathInternal(versionPath);
            
            if (!File.Exists(assetsZipPath))
            {
                Logger.Warning("Cosmetics", $"Assets.zip not found: {assetsZipPath}");
                return null;
            }
            
            var cosmetics = new Dictionary<string, List<string>>();
            
            using var zip = ZipFile.OpenRead(assetsZipPath);
            
            foreach (var (fileName, categoryName) in CosmeticCategoryMap)
            {
                var entryPath = $"Cosmetics/CharacterCreator/{fileName}";
                var entry = zip.GetEntry(entryPath);
                
                if (entry == null)
                {
                    Logger.Info("Cosmetics", $"Entry not found: {entryPath}");
                    continue;
                }
                
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                var items = JsonSerializer.Deserialize<List<CosmeticItem>>(json, JsonOptions);
                if (items != null)
                {
                    var ids = items
                        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                        .Select(item => item.Id!)
                        .ToList();
                    
                    if (ids.Count > 0)
                    {
                        cosmetics[categoryName] = ids;
                        Logger.Info("Cosmetics", $"Loaded {ids.Count} {categoryName} items");
                    }
                }
            }
            
            Logger.Success("Cosmetics", $"Loaded cosmetics from {assetsZipPath}: {cosmetics.Count} categories");
            return cosmetics;
        }
        catch (Exception ex)
        {
            Logger.Error("Cosmetics", $"Failed to load cosmetics: {ex.Message}");
            return null;
        }
    }

    public List<int> GetInstalledVersionsForBranch(string branch)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var result = new HashSet<int>();

        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            // New layout: branch/version
            string branchPath = Path.Combine(root, normalizedBranch);
            if (Directory.Exists(branchPath))
            {
                foreach (var dir in Directory.GetDirectories(branchPath))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, "latest", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsClientPresent(dir))
                        {
                            result.Add(0);
                            Logger.Info("Version", $"Installed versions include latest for {normalizedBranch} at {dir}");
                        }
                        continue;
                    }

                    if (int.TryParse(name, out int version))
                    {
                        if (IsClientPresent(dir))
                        {
                            result.Add(version);
                            Logger.Info("Version", $"Installed version detected: {normalizedBranch}/{version} at {dir}");
                        }
                    }
                }
            }

            // Legacy dash layout: release-29 or release-v29
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.StartsWith(normalizedBranch + "-", StringComparison.OrdinalIgnoreCase)) continue;

                var suffix = name.Substring(normalizedBranch.Length + 1);
                
                // Remove 'v' prefix if present (e.g., "v5" -> "5")
                if (suffix.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    suffix = suffix.Substring(1);
                }

                if (string.Equals(suffix, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsClientPresent(dir))
                    {
                        result.Add(0);
                        Logger.Info("Version", $"Installed legacy latest detected: {name} at {dir}");
                    }
                    continue;
                }

                if (int.TryParse(suffix, out int version))
                {
                    if (IsClientPresent(dir))
                    {
                        result.Add(version);
                        Logger.Info("Version", $"Installed legacy version detected: {name} at {dir}");
                    }
                }
            }
        }
        
        return result.ToList();
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

    /// <summary>
    /// Get information about the pending update, including old version details.
    /// Returns null if no update is pending.
    /// </summary>
    public async Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return null;

            var latestVersion = versions[0];
            var latestPath = GetLatestInstancePath(normalizedBranch);
            var info = LoadLatestInfo(normalizedBranch);
            
            // Check if update is needed
            if (info == null || info.Version == latestVersion) return null;
            
            // Check if old version has userdata
            var oldUserDataPath = Path.Combine(latestPath, "UserData");
            var hasOldUserData = Directory.Exists(oldUserDataPath) && 
                                 Directory.GetFileSystemEntries(oldUserDataPath).Length > 0;
            
            return new UpdateInfo
            {
                OldVersion = info.Version,
                NewVersion = latestVersion,
                HasOldUserData = hasOldUserData,
                Branch = normalizedBranch
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Failed to get pending update info: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copy userdata from one version to another.
    /// </summary>
    public async Task<bool> CopyUserDataAsync(string branch, int fromVersion, int toVersion)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);
            
            // Get source path (if fromVersion is 0, use latest)
            string fromPath;
            if (fromVersion == 0)
            {
                fromPath = GetLatestInstancePath(normalizedBranch);
            }
            else
            {
                fromPath = ResolveInstancePath(normalizedBranch, fromVersion, preferExisting: true);
            }
            
            // Get destination path (if toVersion is 0, use latest)
            string toPath;
            if (toVersion == 0)
            {
                toPath = GetLatestInstancePath(normalizedBranch);
            }
            else
            {
                toPath = ResolveInstancePath(normalizedBranch, toVersion, preferExisting: true);
            }
            
            var fromUserData = Path.Combine(fromPath, "UserData");
            var toUserData = Path.Combine(toPath, "UserData");
            
            if (!Directory.Exists(fromUserData))
            {
                Logger.Warning("UserData", $"Source UserData does not exist: {fromUserData}");
                return false;
            }
            
            // Create destination if needed
            Directory.CreateDirectory(toUserData);
            
            // Copy all contents
            await Task.Run(() => CopyDirectory(fromUserData, toUserData, true));
            
            Logger.Success("UserData", $"Copied UserData from v{fromVersion} to v{toVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UserData", $"Failed to copy userdata: {ex.Message}");
            return false;
        }
    }

    private void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        // Create destination directory
        Directory.CreateDirectory(destDir);
        
        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }
        
        // Copy subdirectories
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir, overwrite);
        }
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

            string versionPath = ResolveInstancePath(branch, isLatestInstance ? 0 : targetVersion, preferExisting: true);
            Directory.CreateDirectory(versionPath);

            // Check if we need to download/install - verify all components
            // The game is installed if the Client executable exists - that's all we need to check
            bool gameIsInstalled = IsClientPresent(versionPath);
            
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
                    var info = LoadLatestInfo(branch);
                    int installedVersion = info?.Version ?? 0;
                    int latestVersion = versions[0];
                    
                    Logger.Info("Download", $"Installed version: {installedVersion}, Latest version: {latestVersion}");
                    
                    // If installed version is different from latest, apply differential update
                    if (installedVersion > 0 && installedVersion != latestVersion)
                    {
                        Logger.Info("Download", $"Differential update available: {installedVersion} -> {latestVersion}");
                        SendProgress(window, "update", 0, $"Updating game from v{installedVersion} to v{latestVersion}...", 0, 0);
                        
                        try
                        {
                            // Apply differential updates for each version step
                            var patchesToApply = GetPatchSequence(installedVersion, latestVersion);
                            Logger.Info("Download", $"Patches to apply: {string.Join(" -> ", patchesToApply)}");
                            
                            for (int i = 0; i < patchesToApply.Count; i++)
                            {
                                int patchVersion = patchesToApply[i];
                                ThrowIfCancelled();
                                
                                // Progress: each patch gets an equal share of 0-90%
                                int baseProgress = (i * 90) / patchesToApply.Count;
                                int progressPerPatch = 90 / patchesToApply.Count;
                                
                                SendProgress(window, "update", baseProgress, $"Downloading patch {i + 1}/{patchesToApply.Count} (v{patchVersion})...", 0, 0);
                                
                                // Ensure Butler is installed
                                await _butlerService.EnsureButlerInstalledAsync((p, m) => { });
                                
                                // Download the PWR patch
                                var patchOs = GetOS();
                                var patchArch = GetArch();
                                var patchBranchType = NormalizeVersionType(branch);
                                string patchUrl = $"https://game-patches.hytale.com/patches/{patchOs}/{patchArch}/{patchBranchType}/0/{patchVersion}.pwr";
                                string patchPwrPath = Path.Combine(_appDir, "cache", $"{branch}_patch_{patchVersion}.pwr");
                                
                                Directory.CreateDirectory(Path.GetDirectoryName(patchPwrPath)!);
                                Logger.Info("Download", $"Downloading patch: {patchUrl}");
                                
                                await DownloadFileAsync(patchUrl, patchPwrPath, (progress, downloaded, total) =>
                                {
                                    int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    SendProgress(window, "update", mappedProgress, $"Downloading patch {i + 1}/{patchesToApply.Count}... {progress}%", downloaded, total);
                                }, _downloadCts.Token);
                                
                                ThrowIfCancelled();
                                
                                // Apply the patch using Butler (differential update)
                                int applyBaseProgress = baseProgress + (progressPerPatch / 2);
                                SendProgress(window, "update", applyBaseProgress, $"Applying patch {i + 1}/{patchesToApply.Count}...", 0, 0);
                                
                                await _butlerService.ApplyPwrAsync(patchPwrPath, versionPath, (progress, message) =>
                                {
                                    int mappedProgress = applyBaseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    SendProgress(window, "update", mappedProgress, message, 0, 0);
                                }, _downloadCts.Token);
                                
                                // Clean up patch file
                                if (File.Exists(patchPwrPath))
                                {
                                    try { File.Delete(patchPwrPath); } catch { }
                                }
                                
                                // Save progress after each patch
                                SaveLatestInfo(branch, patchVersion);
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
                            // Don't fail completely - game is still playable at old version
                            Logger.Warning("Download", "Continuing with existing version...");
                        }
                    }
                    else if (info == null)
                    {
                        // No version info saved, save current
                        SaveLatestInfo(branch, targetVersion);
                    }
                }
                
                // Ensure VC++ Redistributable is installed on Windows before launching
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SendProgress(window, "install", 94, "Checking Visual C++ Runtime...", 0, 0);
                    try
                    {
                        await EnsureVCRedistInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 94 + (int)(progress * 0.02);
                            SendProgress(window, "install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                        // Don't fail - continue anyway
                    }
                }
                
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
                
                SendProgress(window, "complete", 100, "Launching game...", 0, 0);
                try
                {
                    await LaunchGameAsync(versionPath, branch);
                    return new DownloadProgress { Success = true, Progress = 100 };
                }
                catch (Exception ex)
                {
                    Logger.Error("Game", $"Launch failed: {ex.Message}");
                    SendErrorEvent("launch", "Failed to launch game", ex.ToString());
                    return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
                }
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
                }, _downloadCts.Token);
                
                // Clean up PWR file after successful extraction
                if (File.Exists(pwrPath))
                {
                    try { File.Delete(pwrPath); } catch { }
                }
                
                // Skip assets extraction on install to match legacy layout
                ThrowIfCancelled();
            }
            catch (OperationCanceledException)
            {
                // Re-throw cancellation to be handled by outer catch
                throw;
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

            // Ensure VC++ Redistributable is installed on Windows before launching
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SendProgress(window, "install", 95, "Checking Visual C++ Runtime...", 0, 0);
                try
                {
                    await EnsureVCRedistInstalledAsync((progress, message) =>
                    {
                        int mappedProgress = 95 + (int)(progress * 0.01);
                        SendProgress(window, "install", mappedProgress, message, 0, 0);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                    // Don't fail - continue anyway
                }
            }

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
            try
            {
                await LaunchGameAsync(versionPath, branch);
                return new DownloadProgress { Success = true, Progress = 100 };
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Launch failed: {ex.Message}");
                SendErrorEvent("launch", "Failed to launch game", ex.ToString());
                return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
            }
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
        Logger.Info("Download", "CancelDownload called");
        if (_downloadCts != null)
        {
            Logger.Info("Download", "Cancelling download...");
            _downloadCts.Cancel();
            Logger.Info("Download", "Download cancellation requested");
            return true;
        }
        Logger.Warning("Download", "No download in progress to cancel");
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

    // JRE Download - uses official Hytale JRE from launcher.hytale.com
    private const string RequiredJreVersion = "25.0.1_8";
    
    // VC++ Redistributable for Windows
    private const string VCRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    
    /// <summary>
    /// Checks if Visual C++ Redistributable is installed on Windows.
    /// Returns true if installed or not on Windows.
    /// </summary>
    private bool IsVCRedistInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;
        
        try
        {
            // Check for VC++ 2015-2022 Redistributable (x64) in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64");
            if (key != null)
            {
                var installed = key.GetValue("Installed");
                if (installed != null && (int)installed == 1)
                {
                    Logger.Info("VCRedist", "VC++ Redistributable is already installed");
                    return true;
                }
            }
            
            // Also check alternative registry path
            using var key2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Wow6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64");
            if (key2 != null)
            {
                var installed = key2.GetValue("Installed");
                if (installed != null && (int)installed == 1)
                {
                    Logger.Info("VCRedist", "VC++ Redistributable is already installed (WoW64)");
                    return true;
                }
            }
            
            Logger.Info("VCRedist", "VC++ Redistributable not found");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning("VCRedist", $"Failed to check VC++ Redistributable: {ex.Message}");
            return false; // Assume not installed if we can't check
        }
    }
    
    /// <summary>
    /// Ensures Visual C++ Redistributable is installed on Windows.
    /// Downloads and runs the installer if not present.
    /// </summary>
    private async Task EnsureVCRedistInstalledAsync(Action<int, string> progressCallback)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            progressCallback(100, "VC++ not required on this platform");
            return;
        }
        
        if (IsVCRedistInstalled())
        {
            progressCallback(100, "VC++ Redistributable ready");
            return;
        }
        
        progressCallback(0, "Downloading Visual C++ Redistributable...");
        Logger.Info("VCRedist", "Downloading VC++ Redistributable...");
        
        string cacheDir = Path.Combine(_appDir, "cache");
        Directory.CreateDirectory(cacheDir);
        string installerPath = Path.Combine(cacheDir, "vc_redist.x64.exe");
        
        try
        {
            // Download the installer
            using var response = await HttpClient.GetAsync(VCRedistUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    int percent = (int)((downloadedBytes * 50) / totalBytes); // 0-50%
                    progressCallback(percent, $"Downloading VC++ Redistributable... {percent * 2}%");
                }
            }
            
            Logger.Info("VCRedist", "Download complete, running installer...");
            progressCallback(50, "Installing Visual C++ Redistributable...");
            
            // Run the installer silently
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/install /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 || process.ExitCode == 1638) // 1638 = already installed
                {
                    Logger.Success("VCRedist", "VC++ Redistributable installed successfully");
                    progressCallback(100, "VC++ Redistributable installed");
                }
                else if (process.ExitCode == 3010) // Restart required
                {
                    Logger.Success("VCRedist", "VC++ Redistributable installed (restart may be required)");
                    progressCallback(100, "VC++ Redistributable installed");
                }
                else
                {
                    Logger.Warning("VCRedist", $"VC++ installer exited with code: {process.ExitCode}");
                    progressCallback(100, "VC++ installation completed");
                }
            }
            
            // Clean up installer
            try { File.Delete(installerPath); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Error("VCRedist", $"Failed to install VC++ Redistributable: {ex.Message}");
            // Don't fail the game launch - the game might work anyway
            progressCallback(100, "VC++ installation skipped");
        }
    }
    
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
        
        // Check if correct JRE version is installed by looking for version marker file
        string versionMarkerPath = Path.Combine(jreDir, ".jre_version");
        
        if (File.Exists(javaBin) && File.Exists(versionMarkerPath))
        {
            try
            {
                string installedVersion = await File.ReadAllTextAsync(versionMarkerPath);
                if (installedVersion.Trim() == RequiredJreVersion)
                {
                    Logger.Info("JRE", $"Java Runtime {RequiredJreVersion} already installed");
                    EnsureJavaWrapper(javaBin);
                    progressCallback(100, "Java Runtime ready");
                    return;
                }
                Logger.Warning("JRE", $"Installed JRE version {installedVersion.Trim()} != required {RequiredJreVersion}. Reinstalling...");
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to check JRE version: {ex.Message}. Reinstalling...");
            }
        }
        else if (File.Exists(javaBin))
        {
            // Old installation without version marker - reinstall
            Logger.Warning("JRE", "JRE version marker not found. Reinstalling official Hytale JRE...");
        }
        
        // Delete old JRE if exists
        if (Directory.Exists(jreDir))
        {
            try
            {
                Directory.Delete(jreDir, true);
                Logger.Info("JRE", "Removed old JRE installation");
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to remove old JRE: {ex.Message}");
            }
        }
        
        progressCallback(0, "Downloading Java Runtime...");
        Logger.Info("JRE", "Downloading official Hytale Java Runtime...");
        
        // Determine platform - Hytale uses different naming convention
        string osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : 
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        string archiveType = osName == "windows" ? "zip" : "tar.gz";
        
        // First try to fetch latest JRE info from Hytale launcher directly
        string? url = null;
        string? expectedSha256 = null;
        
        try
        {
            Logger.Info("JRE", "Fetching JRE info from launcher.hytale.com...");
            var jreInfoResponse = await HttpClient.GetStringAsync("https://launcher.hytale.com/version/release/jre.json");
            var jreInfo = JsonSerializer.Deserialize<JsonElement>(jreInfoResponse);
            
            if (jreInfo.TryGetProperty("download_url", out var downloadUrls) &&
                downloadUrls.TryGetProperty(osName, out var osUrls) &&
                osUrls.TryGetProperty(arch, out var archInfo))
            {
                if (archInfo.TryGetProperty("url", out var urlProp))
                {
                    url = urlProp.GetString();
                }
                if (archInfo.TryGetProperty("sha256", out var sha256Prop))
                {
                    expectedSha256 = sha256Prop.GetString();
                }
                Logger.Info("JRE", $"Got JRE URL from Hytale launcher: {url}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to fetch from launcher.hytale.com: {ex.Message}");
        }
        
        // Fallback to local jre.json config
        if (string.IsNullOrEmpty(url))
        {
            try
            {
                var jreConfigPath = Path.Combine(AppContext.BaseDirectory, "jre.json");
                if (File.Exists(jreConfigPath))
                {
                    var jreConfigJson = await File.ReadAllTextAsync(jreConfigPath);
                    var jreConfig = JsonSerializer.Deserialize<JsonElement>(jreConfigJson);
                    
                    if (jreConfig.TryGetProperty("download_url", out var downloadUrls) &&
                        downloadUrls.TryGetProperty(osName, out var osUrls) &&
                        osUrls.TryGetProperty(arch, out var archInfo))
                    {
                        if (archInfo.TryGetProperty("url", out var urlProp))
                        {
                            url = urlProp.GetString();
                        }
                        if (archInfo.TryGetProperty("sha256", out var sha256Prop))
                        {
                            expectedSha256 = sha256Prop.GetString();
                        }
                        Logger.Info("JRE", $"Using JRE URL from local config: {url}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to load local jre.json: {ex.Message}");
            }
        }
        
        // Ultimate fallback - hardcoded URLs for official Hytale JRE
        if (string.IsNullOrEmpty(url))
        {
            url = $"https://launcher.hytale.com/redist/jre/{osName}/{arch}/jre-{RequiredJreVersion}.{archiveType}";
            Logger.Info("JRE", $"Using hardcoded Hytale JRE URL: {url}");
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

        // Wrap java to strip unsupported flags and point to the freshly installed JRE
        EnsureJavaWrapper(javaBin);
        
        // Write version marker file to track installed version
        try
        {
            await File.WriteAllTextAsync(versionMarkerPath, RequiredJreVersion);
            Logger.Info("JRE", $"Written version marker: {RequiredJreVersion}");
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to write version marker: {ex.Message}");
        }
        
        progressCallback(100, "Java Runtime installed");
        Logger.Success("JRE", $"Hytale Java Runtime {RequiredJreVersion} installed successfully");
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

            var combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout + "\n" + stderr;
            var match = Regex.Match(combined, "version \"?([0-9][^\"\\s]*)");
            if (match.Success)
            {
                return ParseJavaMajor(match.Groups[1].Value);
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
            // Set working directory to the MacOS folder where the executable is located
            workingDir = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS");
            
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

        // On macOS, clear quarantine attributes BEFORE patching
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
            ClearMacQuarantine(appBundle);
            Logger.Info("Game", "Cleared macOS quarantine attributes before patching");
        }

        // Patch binary to accept custom auth server tokens
        // The auth domain is "sessions.sanasol.ws" but we need to patch "hytale.com" -> "sanasol.ws"
        // so that sessions.hytale.com becomes sessions.sanasol.ws
        // NOTE: Patching is needed even in offline/insecure mode because the game still validates domains
        bool enablePatching = true;
        if (enablePatching && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            try
            {
                // Extract the base domain from auth domain (e.g., "sessions.sanasol.ws" -> "sanasol.ws")
                string baseDomain = _config.AuthDomain;
                if (baseDomain.StartsWith("sessions."))
                {
                    baseDomain = baseDomain.Substring("sessions.".Length);
                }
                
                Logger.Info("Game", $"Patching binary: hytale.com -> {baseDomain}");
                var patcher = new ClientPatcher(baseDomain);
                
                // Patch client binary first
                var patchResult = patcher.EnsureClientPatched(versionPath, (msg, progress) =>
                {
                    if (progress.HasValue)
                    {
                        Logger.Info("Patcher", $"{msg} ({progress}%)");
                    }
                    else
                    {
                        Logger.Info("Patcher", msg);
                    }
                });
                
                // Also patch server JAR (required for singleplayer to work)
                Logger.Info("Game", $"Patching server JAR: sessions.hytale.com -> sessions.{baseDomain}");
                var serverPatchResult = patcher.PatchServerJar(versionPath, (msg, progress) =>
                {
                    if (progress.HasValue)
                    {
                        Logger.Info("Patcher", $"{msg} ({progress}%)");
                    }
                    else
                    {
                        Logger.Info("Patcher", msg);
                    }
                });
                
                if (patchResult.Success)
                {
                    if (patchResult.AlreadyPatched)
                    {
                        Logger.Info("Game", "Client binary already patched");
                    }
                    else if (patchResult.PatchCount > 0)
                    {
                        Logger.Success("Game", $"Client binary patched successfully ({patchResult.PatchCount} occurrences)");
                        
                        // Re-sign the binary after patching (macOS requirement)
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            try
                            {
                                Logger.Info("Game", "Re-signing patched binary...");
                                string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
                                bool signed = ClientPatcher.SignMacOSBinary(appBundle);
                                if (signed)
                                {
                                    Logger.Success("Game", "Binary re-signed successfully");
                                }
                                else
                                {
                                    Logger.Warning("Game", "Binary signing failed - game may not launch");
                                }
                            }
                            catch (Exception signEx)
                            {
                                Logger.Warning("Game", $"Error re-signing binary: {signEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Info("Game", "No client patches needed - binary uses unknown encoding or already patched");
                    }
                }
                else
                {
                    Logger.Warning("Game", $"Client binary patching failed: {patchResult.Error}");
                    Logger.Info("Game", "Continuing launch anyway - may not connect to custom auth server");
                }
                
                // Log server JAR patch result
                if (serverPatchResult.Success)
                {
                    if (serverPatchResult.AlreadyPatched)
                    {
                        Logger.Info("Game", "Server JAR already patched");
                    }
                    else if (serverPatchResult.PatchCount > 0)
                    {
                        Logger.Success("Game", $"Server JAR patched successfully ({serverPatchResult.PatchCount} occurrences)");
                    }
                    else if (!string.IsNullOrEmpty(serverPatchResult.Warning))
                    {
                        Logger.Info("Game", $"Server JAR: {serverPatchResult.Warning}");
                    }
                }
                else
                {
                    Logger.Warning("Game", $"Server JAR patching failed: {serverPatchResult.Error}");
                    Logger.Info("Game", "Singleplayer may not work properly");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Game", $"Error during binary patching: {ex.Message}");
                Logger.Info("Game", "Continuing launch anyway - may not connect to custom auth server");
            }
        }

        // STEP 1: Determine UUID to use for this session
        // This must happen BEFORE auth token fetching so we use the correct UUID
        string baseUuid;
        string sessionUuid; // The UUID to actually use for this game session
        
        if (string.IsNullOrWhiteSpace(_config.UUID))
        {
            baseUuid = GenerateOfflineUUID(_config.Nick);
            _config.UUID = baseUuid;
            SaveConfig();
            Logger.Info("Config", $"Generated and saved UUID from nickname: {baseUuid}");
        }
        else
        {
            baseUuid = _config.UUID;
        }
        
        // Always use the saved UUID to preserve player items/progress
        sessionUuid = baseUuid;
        Logger.Info("Game", $"Using saved UUID: {sessionUuid}");

        // STEP 2: Fetch auth token - only if OnlineMode is enabled
        // If user wants offline mode, skip token fetching entirely
        string? identityToken = null;
        string? sessionToken = null;
        
        if (_config.OnlineMode && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            Logger.Info("Game", $"Online mode enabled - fetching auth tokens from {_config.AuthDomain}...");
            
            try
            {
                var authService = new AuthService(HttpClient, _config.AuthDomain);
                
                // Use sessionUuid (which may be fresh/random) for auth
                var tokenResult = await authService.GetGameSessionTokenAsync(sessionUuid, _config.Nick);
                
                if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Token))
                {
                    identityToken = tokenResult.Token;
                    sessionToken = tokenResult.SessionToken ?? tokenResult.Token; // Fallback to identity token
                    Logger.Success("Game", "Identity token obtained successfully");
                    Logger.Success("Game", "Session token obtained successfully");
                }
                else
                {
                    Logger.Warning("Game", $"Could not get auth token: {tokenResult.Error}");
                    Logger.Info("Game", "Will try launching with offline mode instead");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Error fetching auth token: {ex.Message}");
                Logger.Info("Game", "Will try launching with offline mode instead");
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

        // Use per-instance UserData folder - this keeps skins/settings with the game instance
        string userDataDir = GetInstanceUserDataPath(versionPath);
        Directory.CreateDirectory(userDataDir);
        
        // Restore current profile's skin data before launching the game
        // This ensures the player's custom skin is loaded from their profile
        var currentProfile = _config.Profiles?.FirstOrDefault(p => p.UUID == sessionUuid);
        if (currentProfile != null)
        {
            RestoreProfileSkinData(currentProfile);
            Logger.Info("Game", $"Restored skin data for profile '{currentProfile.Name}'");
        }

        Logger.Info("Game", $"Launching: {executable}");
        Logger.Info("Game", $"Java: {javaPath}");
        Logger.Info("Game", $"AppDir: {gameDir}");
        Logger.Info("Game", $"UserData: {userDataDir}");
        Logger.Info("Game", $"Online Mode: {_config.OnlineMode}");
        Logger.Info("Game", $"Session UUID: {sessionUuid}");

        // On macOS/Linux, create a launch script to run with clean environment
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
            
            // Add arguments using ArgumentList for proper Windows escaping
            startInfo.ArgumentList.Add("--app-dir");
            startInfo.ArgumentList.Add(gameDir);
            startInfo.ArgumentList.Add("--user-dir");
            startInfo.ArgumentList.Add(userDataDir);
            startInfo.ArgumentList.Add("--java-exec");
            startInfo.ArgumentList.Add(javaPath);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(_config.Nick);
            
            // Add auth mode based on user's OnlineMode preference
            // If OnlineMode is OFF, always use offline mode regardless of tokens
            // If OnlineMode is ON and we have tokens, use authenticated mode
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
                // Offline mode - either user selected it or no tokens available
                startInfo.ArgumentList.Add("--auth-mode");
                startInfo.ArgumentList.Add("offline");
                startInfo.ArgumentList.Add("--uuid");
                startInfo.ArgumentList.Add(sessionUuid);
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            // Log the arguments for debugging
            Logger.Info("Game", $"Windows launch args: {string.Join(" ", startInfo.ArgumentList)}");
        }
        else
        {
            // Build arguments for the launch script - use only documented game arguments
            var gameArgs = new List<string>
            {
                $"--app-dir \"{gameDir}\"",
                $"--user-dir \"{userDataDir}\"",
                $"--java-exec \"{javaPath}\"",
                $"--name \"{_config.Nick}\""
            };
            
            // Add auth mode based on user's OnlineMode preference
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
                // Offline mode - either user selected it or no tokens available
                gameArgs.Add("--auth-mode offline");
                gameArgs.Add($"--uuid \"{sessionUuid}\"");
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            // macOS/Linux: Use env to run with completely clean environment
            // This prevents .NET runtime environment variables from interfering
            string argsString = string.Join(" ", gameArgs);
            string launchScript = Path.Combine(versionPath, "launch.sh");
            
            string homeDir = Environment.GetEnvironmentVariable("HOME") ?? "/Users/" + Environment.UserName;
            string userName = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
            
            // Get the Client directory for LD_LIBRARY_PATH (needed for shared libraries like SDL3_image.so)
            string clientDir = Path.Combine(versionPath, "Client");
            
            // Write the launch script with env to start with empty environment
            string scriptContent = $@"#!/bin/bash
# Launch script generated by HyPrism
# Uses env to clear ALL environment variables before launching game

# Set LD_LIBRARY_PATH to include Client directory for shared libraries (SDL3_image.so, etc.)
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
            
            // Use /bin/bash to run the script
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
            _gameProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Failed to start game process: {ex.Message}");
            SendErrorEvent("launch", "Failed to start game", ex.Message);
            throw new Exception($"Failed to start game: {ex.Message}");
        }
        
        if (_gameProcess == null)
        {
            Logger.Error("Game", "Process.Start returned null - game failed to launch");
            SendErrorEvent("launch", "Failed to start game", "Process.Start returned null");
            throw new Exception("Failed to start game process");
        }
        
        Logger.Success("Game", $"Game started with PID: {_gameProcess.Id}");
        
        // Set Discord presence to Playing
        _discordService.SetPresence(DiscordService.PresenceState.Playing, $"Playing as {_config.Nick}");
        
        // Notify frontend that game has launched
        SendGameStateEvent("started");
        
        // Handle process exit in background
        _ = Task.Run(async () =>
        {
            await _gameProcess.WaitForExitAsync();
            var exitCode = _gameProcess.ExitCode;
            Logger.Info("Game", $"Game process exited with code: {exitCode}");
            _gameProcess = null;
            
            // Backup current profile's skin data after game exits (save any changes made during gameplay)
            BackupProfileSkinData(_config.UUID);
            
            // Set Discord presence back to Idle
            _discordService.SetPresence(DiscordService.PresenceState.Idle);
            
            // Notify frontend that game has exited with exit code
            SendGameStateEvent("stopped", exitCode);
        });
    }

    private void SendGameStateEvent(string state, int? exitCode = null)
    {
        if (_mainWindow == null) return;
        
        try
        {
            var eventData = new
            {
                type = "event",
                eventName = "game-state",
                data = new { state, exitCode }
            };
            _mainWindow.SendWebMessage(JsonSerializer.Serialize(eventData, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to send game state event: {ex.Message}");
        }
    }

    private void SendErrorEvent(string type, string message, string? technical = null)
    {
        if (_mainWindow == null) return;

        try
        {
            var eventData = new
            {
                type = "event",
                eventName = "error",
                data = new
                {
                    type,
                    message,
                    technical,
                    timestamp = DateTimeOffset.UtcNow
                }
            };
            _mainWindow.SendWebMessage(JsonSerializer.Serialize(eventData, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Warning("Events", $"Failed to send error event: {ex.Message}");
        }
    }
    
    public void SetMainWindow(PhotinoWindow window)
    {
        _mainWindow = window;
    }

    // Check for launcher updates and emit event if available
    public async Task CheckForLauncherUpdatesAsync()
    {
        if (_mainWindow == null) return;

        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            
            // Get all releases (not just latest) to support beta channel
            // TESTING: Using TEST repo instead of HyPrism
            var apiUrl = "https://api.github.com/repos/yyyumeniku/TEST/releases?per_page=50";
            var json = await HttpClient.GetStringAsync(apiUrl);
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
                // Formats: "v2.0.1", "2.0.1", "beta3-3.0.0", "beta-3.0.0"
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

                var eventData = new
                {
                    type = "event",
                    eventName = "update:available",
                    data = new
                    {
                        version = bestVersion,
                        currentVersion = currentVersion,
                        downloadUrl = downloadUrl ?? "",
                        assetName = assetName ?? "",
                        releaseUrl = release.GetProperty("html_url").GetString() ?? "",
                        isBeta = launcherBranch == "beta"
                    }
                };
                _mainWindow.SendWebMessage(JsonSerializer.Serialize(eventData, JsonOptions));
            }
            else
            {
                Logger.Info("Update", $"Launcher is up to date: {currentVersion} (channel: {launcherBranch})");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Failed to check for updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse version string from various tag formats.
    /// Supports: "v2.0.1", "2.0.1", "beta3-3.0.0", "beta-3.0.0", "beta3-v3.0.0"
    /// </summary>
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
            string versionPath = ResolveInstancePath(normalizedBranch, versionNumber, preferExisting: true);
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
        // Folder picker is not available in Photino. Return the current/active
        // instance root so the frontend can show it and collect user input manually.
        return Task.FromResult<string?>(GetInstanceRoot());
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
        // TESTING: Using TEST repo instead of HyPrism
        const string releasesPage = "https://github.com/yyyumeniku/TEST/releases/latest";

        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            var currentVersion = GetLauncherVersion();
            
            // Get all releases to find the best match for user's channel
            // TESTING: Using TEST repo instead of HyPrism
            var apiUrl = "https://api.github.com/repos/yyyumeniku/TEST/releases?per_page=50";
            var json = await HttpClient.GetStringAsync(apiUrl);
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
                BrowserOpenURL(releasesPage);
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

            // Platform-specific installation
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    Logger.Info("Update", "Mounting DMG and installing...");
                    
                    // Mount the DMG
                    var mountProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "hdiutil",
                        Arguments = $"attach \"{targetPath}\" -nobrowse -readonly",
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
                    // currentExe is likely: /Applications/HyPrism.app/Contents/MacOS/HyPrism
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
rm -f ""{targetPath}""
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
                catch (Exception macEx)
                {
                    Logger.Error("Update", $"Auto-update failed: {macEx.Message}");
                    // Fallback: open the DMG manually
                    try { Process.Start("open", targetPath); } catch { }
                    throw new Exception($"Please install the update manually from Downloads. {macEx.Message}");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Get current executable path
                    var currentExe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(currentExe))
                    {
                        Logger.Error("Update", "Could not determine current executable path");
                        Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
                        return true;
                    }

                    // Create a batch script to replace the exe and restart
                    var batchPath = Path.Combine(Path.GetTempPath(), "hyprism_update.bat");
                    var batchContent = $@"@echo off
timeout /t 2 /nobreak >nul
del ""{currentExe}"" 2>nul
move /y ""{targetPath}"" ""{currentExe}""
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

                    // Exit this application so the batch can replace the exe
                    Logger.Info("Update", "Starting update script and exiting...");
                    Environment.Exit(0);
                }
                catch (Exception updateEx)
                {
                    Logger.Warning("Update", $"Auto-update failed, opening Explorer: {updateEx.Message}");
                    Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
                catch (Exception linuxEx)
                {
                    Logger.Error("Update", $"Auto-update failed: {linuxEx.Message}");
                    try { Process.Start("xdg-open", targetPath); } catch { }
                    throw new Exception($"Please install the update manually from Downloads. {linuxEx.Message}");
                }
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

    // Launcher Branch (release/beta update channel)
    public string GetLauncherBranch() => string.IsNullOrWhiteSpace(_config.LauncherBranch) ? "release" : _config.LauncherBranch;
    
    public bool SetLauncherBranch(string branch)
    {
        var normalizedBranch = branch?.ToLowerInvariant() ?? "release";
        if (normalizedBranch != "release" && normalizedBranch != "beta")
        {
            normalizedBranch = "release";
        }
        _config.LauncherBranch = normalizedBranch;
        SaveConfig();
        Logger.Info("Config", $"Launcher branch set to: {normalizedBranch}");
        return true;
    }

    // Close After Launch setting
    public bool GetCloseAfterLaunch() => _config.CloseAfterLaunch;
    
    public bool SetCloseAfterLaunch(bool enabled)
    {
        _config.CloseAfterLaunch = enabled;
        SaveConfig();
        Logger.Info("Config", $"Close after launch set to: {enabled}");
        return true;
    }

    // Discord Announcements settings
    public bool GetShowDiscordAnnouncements() => _config.ShowDiscordAnnouncements;
    
    public bool SetShowDiscordAnnouncements(bool enabled)
    {
        _config.ShowDiscordAnnouncements = enabled;
        SaveConfig();
        Logger.Info("Config", $"Show Discord announcements set to: {enabled}");
        return true;
    }

    public bool IsAnnouncementDismissed(string announcementId)
    {
        return _config.DismissedAnnouncementIds.Contains(announcementId);
    }

    public bool DismissAnnouncement(string announcementId)
    {
        if (!_config.DismissedAnnouncementIds.Contains(announcementId))
        {
            _config.DismissedAnnouncementIds.Add(announcementId);
            SaveConfig();
            Logger.Info("Discord", $"Announcement {announcementId} dismissed");
        }
        return true;
    }

    // News settings
    public bool GetDisableNews() => _config.DisableNews;
    
    public bool SetDisableNews(bool disabled)
    {
        _config.DisableNews = disabled;
        SaveConfig();
        Logger.Info("Config", $"News disabled set to: {disabled}");
        return true;
    }

    // Background settings
    public string GetBackgroundMode() => _config.BackgroundMode;
    
    public bool SetBackgroundMode(string mode)
    {
        _config.BackgroundMode = mode;
        SaveConfig();
        Logger.Info("Config", $"Background mode set to: {mode}");
        return true;
    }

    // Accent color settings
    public string GetAccentColor() => _config.AccentColor;
    
    public bool SetAccentColor(string color)
    {
        _config.AccentColor = color;
        SaveConfig();
        Logger.Info("Config", $"Accent color set to: {color}");
        return true;
    }

    // Onboarding state
    public bool GetHasCompletedOnboarding() => _config.HasCompletedOnboarding;
    
    public bool SetHasCompletedOnboarding(bool completed)
    {
        _config.HasCompletedOnboarding = completed;
        SaveConfig();
        Logger.Info("Config", $"Onboarding completed: {completed}");
        return true;
    }

    /// <summary>
    /// Generates a random username for the onboarding flow.
    /// </summary>
    public string GetRandomUsername()
    {
        return GenerateRandomUsername();
    }

    /// <summary>
    /// Resets the onboarding so it will show again on next launch.
    /// </summary>
    public bool ResetOnboarding()
    {
        _config.HasCompletedOnboarding = false;
        SaveConfig();
        Logger.Info("Config", "Onboarding reset - will show on next launch");
        return true;
    }

    // Online mode settings
    public bool GetOnlineMode() => _config.OnlineMode;
    
    public bool SetOnlineMode(bool online)
    {
        _config.OnlineMode = online;
        SaveConfig();
        Logger.Info("Config", $"Online mode set to: {online}");
        return true;
    }
    
    // Auth domain settings
    public string GetAuthDomain() => _config.AuthDomain;
    
    public bool SetAuthDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "sessions.sanasol.ws";
        }
        _config.AuthDomain = domain;
        SaveConfig();
        Logger.Info("Config", $"Auth domain set to: {domain}");
        return true;
    }

    /// <summary>
    /// Gets the list of available background filenames
    /// </summary>
    public List<string> GetAvailableBackgrounds()
    {
        var backgrounds = new List<string>();
        // These match the backgrounds in the frontend assets
        for (int i = 1; i <= 30; i++)
        {
            backgrounds.Add($"bg_{i}");
        }
        return backgrounds;
    }

    // Launcher Data Directory settings
    public string GetLauncherDataDirectory() => _config.LauncherDataDirectory;
    
    public Task<string?> SetLauncherDataDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom launcher data directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _config.LauncherDataDirectory = "";
                SaveConfig();
                Logger.Success("Config", "Launcher data directory cleared, will use default on next restart");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(expanded);
            }

            // Just save the path, the change takes effect on next restart
            _config.LauncherDataDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Launcher data directory set to {expanded} (takes effect on restart)");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set launcher data directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

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
    
    /// <summary>
    /// Installs a mod file from base64 content (used for drag-and-drop from browser).
    /// </summary>
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string branch, int version)
    {
        try
        {
            Logger.Info("Mods", $"Installing mod from base64: {fileName}, content length: {base64Content?.Length ?? 0}");
            
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(base64Content))
            {
                Logger.Error("Mods", "Invalid file name or content");
                return false;
            }
            
            // Decode base64 content
            byte[] fileBytes;
            try
            {
                // The content should be pure base64 (no data URL prefix)
                fileBytes = Convert.FromBase64String(base64Content);
                Logger.Success("Mods", $"Decoded base64 content: {fileBytes.Length} bytes");
            }
            catch (Exception ex)
            {
                Logger.Error("Mods", $"Failed to decode base64 content: {ex.Message}");
                return false;
            }
            
            // Get the target instance path and create mods directory
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            
            var existingPath = FindExistingInstancePath(resolvedBranch, version);
            string versionPath;
            
            if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
            {
                versionPath = existingPath;
            }
            else
            {
                versionPath = GetInstancePath(resolvedBranch, version);
                Logger.Info("Mods", $"Instance not found, creating mod directory at: {versionPath}");
            }
            
            string modsPath = GetModsPath(versionPath);
            var destPath = Path.Combine(modsPath, fileName);
            
            // Write the file
            await File.WriteAllBytesAsync(destPath, fileBytes);
            Logger.Success("Mods", $"Saved mod from drag-drop: {fileName} ({fileBytes.Length} bytes)");
            
            // Generate a local ID based on filename
            var localId = $"local-{Path.GetFileNameWithoutExtension(fileName)}";
            var modName = Path.GetFileNameWithoutExtension(fileName);
            
            // Try to extract version from filename
            var versionMatch = System.Text.RegularExpressions.Regex.Match(modName, @"[-_]v?(\d+(?:\.\d+)*(?:-\w+)?)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string modVersion = versionMatch.Success ? versionMatch.Groups[1].Value : "1.0";
            
            if (versionMatch.Success)
            {
                modName = modName.Substring(0, versionMatch.Index).TrimEnd('-', '_', ' ');
            }
            
            modName = modName.Replace('_', ' ').Replace('-', ' ');
            
            var newMod = new InstalledMod
            {
                Id = localId,
                Name = modName,
                FileName = fileName,
                Enabled = true,
                Version = modVersion,
                Author = "",
                Description = ""
            };
            
            // Try to look up mod info on CurseForge using fingerprint
            try
            {
                Logger.Info("Mods", $"Looking up mod metadata on CurseForge for: {fileName}");
                var cfInfo = await LookupModOnCurseForgeAsync(destPath);
                if (cfInfo != null)
                {
                    Logger.Success("Mods", $"Found CurseForge match for {fileName}: {cfInfo.Name}");
                    Logger.Info("Mods", $"  - Mod ID: {cfInfo.ModId}, File ID: {cfInfo.FileId}");
                    Logger.Info("Mods", $"  - Author: {cfInfo.Author}, Version: {cfInfo.Version}");
                    newMod.CurseForgeId = cfInfo.ModId;
                    newMod.FileId = cfInfo.FileId;
                    newMod.Name = cfInfo.Name ?? modName;
                    newMod.Version = cfInfo.Version ?? modVersion;
                    newMod.Author = cfInfo.Author ?? "";
                    newMod.Description = cfInfo.Description ?? "";
                    newMod.IconUrl = cfInfo.IconUrl ?? "";
                    newMod.Id = cfInfo.ModId;
                }
                else
                {
                    Logger.Warning("Mods", $"No CurseForge match found for {fileName} - using local metadata");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Mods", $"Could not look up mod on CurseForge: {ex.Message}");
            }
            
            // Update manifest
            await _modManifestLock.WaitAsync();
            try
            {
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
                
                installedMods.RemoveAll(m => m.Id == localId || m.FileName == fileName || (newMod.CurseForgeId != null && m.CurseForgeId == newMod.CurseForgeId));
                installedMods.Add(newMod);
                
                var manifestOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(installedMods, manifestOptions));
            }
            finally
            {
                _modManifestLock.Release();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to install mod from base64: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Installs a local mod file (JAR) to the specified instance by copying it to the mods folder.
    /// Also attempts to look up mod info on CurseForge using fingerprinting.
    /// Creates the mods directory if the instance doesn't exist yet.
    /// </summary>
    public async Task<bool> InstallLocalModFile(string sourcePath, string branch, int version)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Logger.Error("Mods", $"Source file not found: {sourcePath}");
                return false;
            }
            
            var fileName = Path.GetFileName(sourcePath);
            
            // Get the target instance path and create mods directory
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            
            // Try to find existing instance, or use default path
            var existingPath = FindExistingInstancePath(resolvedBranch, version);
            string versionPath;
            
            if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
            {
                versionPath = existingPath;
            }
            else
            {
                // Instance doesn't exist yet - use the default path and create the directory
                versionPath = GetInstancePath(resolvedBranch, version);
                Logger.Info("Mods", $"Instance not found, creating mod directory at: {versionPath}");
            }
            
            string modsPath = GetModsPath(versionPath);
            
            var destPath = Path.Combine(modsPath, fileName);
            
            // Copy the file
            File.Copy(sourcePath, destPath, overwrite: true);
            Logger.Success("Mods", $"Copied local mod: {fileName}");
            
            // Generate a local ID based on filename
            var localId = $"local-{Path.GetFileNameWithoutExtension(fileName)}";
            var modName = Path.GetFileNameWithoutExtension(fileName);
            
            // Try to extract version from filename (common patterns: ModName-1.0.0, ModName_v1.2)
            var versionMatch = System.Text.RegularExpressions.Regex.Match(modName, @"[-_]v?(\d+(?:\.\d+)*(?:-\w+)?)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string modVersion = versionMatch.Success ? versionMatch.Groups[1].Value : "1.0";
            
            // Clean up the mod name (remove version suffix if found)
            if (versionMatch.Success)
            {
                modName = modName.Substring(0, versionMatch.Index).TrimEnd('-', '_', ' ');
            }
            
            // Replace underscores/hyphens with spaces for display
            modName = modName.Replace('_', ' ').Replace('-', ' ');
            
            // Create new mod entry
            var newMod = new InstalledMod
            {
                Id = localId,
                Name = modName,
                FileName = fileName,
                Enabled = true,
                Version = modVersion,
                Author = "",
                Description = ""
            };
            
            // Try to look up mod info on CurseForge using fingerprint
            try
            {
                Logger.Info("Mods", $"Looking up mod metadata on CurseForge for: {fileName}");
                var cfInfo = await LookupModOnCurseForgeAsync(destPath);
                if (cfInfo != null)
                {
                    Logger.Success("Mods", $"Found CurseForge match for {fileName}: {cfInfo.Name}");
                    Logger.Info("Mods", $"  - Mod ID: {cfInfo.ModId}, File ID: {cfInfo.FileId}");
                    Logger.Info("Mods", $"  - Author: {cfInfo.Author}, Version: {cfInfo.Version}");
                    newMod.CurseForgeId = cfInfo.ModId;
                    newMod.FileId = cfInfo.FileId;
                    newMod.Name = cfInfo.Name ?? modName;
                    newMod.Version = cfInfo.Version ?? modVersion;
                    newMod.Author = cfInfo.Author ?? "";
                    newMod.Description = cfInfo.Description ?? "";
                    newMod.IconUrl = cfInfo.IconUrl ?? "";
                    newMod.Id = cfInfo.ModId; // Use CurseForge ID instead of local ID
                }
                else
                {
                    Logger.Warning("Mods", $"No CurseForge match found for {fileName} - using local metadata");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Mods", $"Could not look up mod on CurseForge: {ex.Message}");
            }
            
            // Update manifest with lock to prevent concurrent write issues
            await _modManifestLock.WaitAsync();
            try
            {
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
                
                // Remove existing entry if updating
                installedMods.RemoveAll(m => m.Id == localId || m.FileName == fileName || (newMod.CurseForgeId != null && m.CurseForgeId == newMod.CurseForgeId));
                
                installedMods.Add(newMod);
                
                var manifestOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(installedMods, manifestOptions));
            }
            finally
            {
                _modManifestLock.Release();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to install local mod: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// CurseForge mod info from fingerprint lookup
    /// </summary>
    private class CurseForgeModInfo
    {
        public string ModId { get; set; } = "";
        public string FileId { get; set; } = "";
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
    }
    
    /// <summary>
    /// Computes the CurseForge fingerprint (MurmurHash2) for a file.
    /// CurseForge uses a normalized version of the file (whitespace removed for certain file types).
    /// </summary>
    private static uint ComputeCurseForgeFingerprint(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        
        // CurseForge removes whitespace from the file for fingerprinting
        // Filter out whitespace bytes (0x09, 0x0A, 0x0D, 0x20)
        var filtered = bytes.Where(b => b != 0x09 && b != 0x0A && b != 0x0D && b != 0x20).ToArray();
        
        return MurmurHash2(filtered, 1);
    }
    
    /// <summary>
    /// MurmurHash2 implementation matching CurseForge's fingerprinting.
    /// </summary>
    private static uint MurmurHash2(byte[] data, uint seed)
    {
        const uint m = 0x5bd1e995;
        const int r = 24;
        
        uint h = seed ^ (uint)data.Length;
        int len = data.Length;
        int i = 0;
        
        while (len >= 4)
        {
            uint k = BitConverter.ToUInt32(data, i);
            k *= m;
            k ^= k >> r;
            k *= m;
            h *= m;
            h ^= k;
            i += 4;
            len -= 4;
        }
        
        switch (len)
        {
            case 3: h ^= (uint)data[i + 2] << 16; goto case 2;
            case 2: h ^= (uint)data[i + 1] << 8; goto case 1;
            case 1: h ^= data[i]; h *= m; break;
        }
        
        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;
        
        return h;
    }
    
    /// <summary>
    /// Looks up a mod file on CurseForge using its fingerprint.
    /// </summary>
    private async Task<CurseForgeModInfo?> LookupModOnCurseForgeAsync(string filePath)
    {
        try
        {
            var fingerprint = ComputeCurseForgeFingerprint(filePath);
            Logger.Info("Mods", $"Looking up file with fingerprint: {fingerprint}");
            
            // Call CurseForge fingerprint API
            var requestBody = new { fingerprints = new[] { fingerprint } };
            var requestJson = JsonSerializer.Serialize(requestBody);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1/fingerprints");
            request.Headers.Add("x-api-key", "$2a$10$1W4EvLWzLe4.RM1kcxW9n.vxmBPEYcg9dvpT4r5OAlkQk/.6jQE4e");
            request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            
            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("Mods", $"CurseForge fingerprint lookup failed: {response.StatusCode}");
                return null;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var data = doc.RootElement.GetProperty("data");
            var exactMatches = data.GetProperty("exactMatches");
            
            if (exactMatches.GetArrayLength() == 0)
            {
                Logger.Info("Mods", "No exact fingerprint match found on CurseForge");
                return null;
            }
            
            var match = exactMatches[0];
            var file = match.GetProperty("file");
            var modId = file.GetProperty("modId").GetInt32();
            var fileId = file.GetProperty("id").GetInt32();
            var displayName = file.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            
            // Get mod details for more info
            var modUrl = $"https://api.curseforge.com/v1/mods/{modId}";
            using var modRequest = new HttpRequestMessage(HttpMethod.Get, modUrl);
            modRequest.Headers.Add("x-api-key", "$2a$10$1W4EvLWzLe4.RM1kcxW9n.vxmBPEYcg9dvpT4r5OAlkQk/.6jQE4e");
            
            using var modResponse = await HttpClient.SendAsync(modRequest);
            if (!modResponse.IsSuccessStatusCode)
            {
                // Return basic info if mod details fail
                return new CurseForgeModInfo
                {
                    ModId = modId.ToString(),
                    FileId = fileId.ToString(),
                    Version = displayName
                };
            }
            
            var modJson = await modResponse.Content.ReadAsStringAsync();
            var modData = JsonSerializer.Deserialize<CurseForgeModResponse>(modJson, JsonOptions);
            var mod = modData?.Data;
            
            return new CurseForgeModInfo
            {
                ModId = modId.ToString(),
                FileId = fileId.ToString(),
                Name = mod?.Name,
                Version = displayName,
                Author = mod?.Authors?.FirstOrDefault()?.Name,
                Description = mod?.Summary,
                IconUrl = mod?.Logo?.ThumbnailUrl
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"CurseForge lookup error: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Exports the mod list for an instance as a JSON file.
    /// Returns the path to the exported file.
    /// </summary>
    public string? ExportModList(string branch, int version)
    {
        try
        {
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
            string modsPath = GetModsPath(versionPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            if (!File.Exists(manifestPath))
            {
                Logger.Warning("Mods", "No manifest found to export");
                return null;
            }
            
            var manifestJson = File.ReadAllText(manifestPath);
            var installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson, JsonOptions) ?? new List<InstalledMod>();
            
            // Create export data with only CurseForge mods that can be re-downloaded
            var exportData = installedMods
                .Where(m => !string.IsNullOrEmpty(m.CurseForgeId) && !string.IsNullOrEmpty(m.FileId))
                .Select(m => new
                {
                    curseForgeId = m.CurseForgeId,
                    fileId = m.FileId,
                    name = m.Name,
                    version = m.Version
                })
                .ToList();
            
            if (exportData.Count == 0)
            {
                Logger.Warning("Mods", "No CurseForge mods to export");
                return null;
            }
            
            // Save to Downloads folder
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var exportFileName = $"HyPrism-ModList-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var exportPath = Path.Combine(downloadsPath, exportFileName);
            
            var exportOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(exportPath, JsonSerializer.Serialize(exportData, exportOptions));
            
            Logger.Success("Mods", $"Exported {exportData.Count} mods to {exportPath}");
            return exportPath;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to export mod list: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Gets the last export path or Desktop as default.
    /// </summary>
    public string GetLastExportPath()
    {
        if (!string.IsNullOrWhiteSpace(_config.LastExportPath) && Directory.Exists(_config.LastExportPath))
        {
            return _config.LastExportPath;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }
    
    /// <summary>
    /// Exports mods to a specified directory with the given export type.
    /// ExportType: "modlist" for JSON file, "zip" for ZIP archive of all mod files.
    /// Returns the path to the exported file or null on failure.
    /// </summary>
    public Task<string?> ExportModsToFolder(string branch, int version, string targetFolder, string exportType)
    {
        try
        {
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
            string modsPath = GetModsPath(versionPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            // Save the export path for next time
            _config.LastExportPath = targetFolder;
            SaveConfig();
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            
            if (exportType == "zip")
            {
                // Export as ZIP file containing all mod files
                var modFiles = Directory.Exists(modsPath) 
                    ? Directory.GetFiles(modsPath).Where(f => !f.EndsWith(".json")).ToArray()
                    : Array.Empty<string>();
                
                if (modFiles.Length == 0)
                {
                    Logger.Warning("Mods", "No mod files to export as ZIP");
                    return Task.FromResult<string?>(null);
                }
                
                var zipFileName = $"HyPrism-Mods-{timestamp}.zip";
                var zipPath = Path.Combine(targetFolder, zipFileName);
                
                // Create ZIP archive
                using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var modFile in modFiles)
                    {
                        archive.CreateEntryFromFile(modFile, Path.GetFileName(modFile));
                    }
                    
                    // Also include manifest.json if it exists
                    if (File.Exists(manifestPath))
                    {
                        archive.CreateEntryFromFile(manifestPath, "manifest.json");
                    }
                }
                
                Logger.Success("Mods", $"Exported {modFiles.Length} mod files to {zipPath}");
                return Task.FromResult<string?>(zipPath);
            }
            else
            {
                // Export as JSON modlist (default)
                if (!File.Exists(manifestPath))
                {
                    Logger.Warning("Mods", "No manifest found to export");
                    return Task.FromResult<string?>(null);
                }
                
                var manifestJson = File.ReadAllText(manifestPath);
                var installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson, JsonOptions) ?? new List<InstalledMod>();
                
                // Create export data with only CurseForge mods that can be re-downloaded
                var exportData = installedMods
                    .Where(m => !string.IsNullOrEmpty(m.CurseForgeId) && !string.IsNullOrEmpty(m.FileId))
                    .Select(m => new
                    {
                        curseForgeId = m.CurseForgeId,
                        fileId = m.FileId,
                        name = m.Name,
                        version = m.Version
                    })
                    .ToList();
                
                if (exportData.Count == 0)
                {
                    Logger.Warning("Mods", "No CurseForge mods to export as modlist");
                    return Task.FromResult<string?>(null);
                }
                
                var exportFileName = $"HyPrism-ModList-{timestamp}.json";
                var exportPath = Path.Combine(targetFolder, exportFileName);
                
                var exportOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(exportPath, JsonSerializer.Serialize(exportData, exportOptions));
                
                Logger.Success("Mods", $"Exported {exportData.Count} mods to {exportPath}");
                return Task.FromResult<string?>(exportPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to export mods: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
    
    /// <summary>
    /// Imports and downloads mods from a mod list JSON file.
    /// Returns the number of mods successfully imported.
    /// </summary>
    public async Task<int> ImportModList(string modListPath, string branch, int version)
    {
        try
        {
            if (!File.Exists(modListPath))
            {
                Logger.Error("Mods", $"Mod list file not found: {modListPath}");
                return 0;
            }
            
            var json = File.ReadAllText(modListPath);
            var modList = JsonSerializer.Deserialize<List<ModListEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (modList == null || modList.Count == 0)
            {
                Logger.Warning("Mods", "No mods found in import file");
                return 0;
            }
            
            int successCount = 0;
            foreach (var mod in modList)
            {
                if (string.IsNullOrEmpty(mod.CurseForgeId) || string.IsNullOrEmpty(mod.FileId))
                {
                    Logger.Warning("Mods", $"Skipping mod with missing IDs: {mod.Name ?? "Unknown"}");
                    continue;
                }
                
                try
                {
                    var success = await InstallModFileToInstanceAsync(mod.CurseForgeId, mod.FileId, branch, version);
                    if (success)
                    {
                        successCount++;
                        Logger.Success("Mods", $"Imported: {mod.Name ?? mod.CurseForgeId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Mods", $"Failed to import {mod.Name ?? mod.CurseForgeId}: {ex.Message}");
                }
            }
            
            Logger.Success("Mods", $"Imported {successCount}/{modList.Count} mods");
            return successCount;
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to import mod list: {ex.Message}");
            return 0;
        }
    }

    public Task<string?> BrowseFolder(string? initialPath = null)
    {
        return BrowseFolderAsync(initialPath);
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
        string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
        if (!Directory.Exists(versionPath)) return result;
        
        string modsPath = GetModsPath(versionPath);
        
        if (!Directory.Exists(modsPath)) return result;
        
        // First, load mods from manifest
        var manifestMods = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(modsPath, "manifest.json");
        
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, JsonOptions) ?? new List<InstalledMod>();
                foreach (var mod in mods)
                {
                    // Normalize IDs and ensure CurseForgeId and screenshots are populated
                    if (!string.IsNullOrEmpty(mod.CurseForgeId) && !mod.Id.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
                    {
                        mod.Id = $"cf-{mod.CurseForgeId}";
                    }
                    else if (string.IsNullOrEmpty(mod.CurseForgeId) && mod.Id.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
                    {
                        mod.CurseForgeId = mod.Id.Replace("cf-", "");
                    }
                    mod.Screenshots ??= new List<CurseForgeScreenshot>();
                    
                    // Track by filename for merging with JAR scan
                    if (!string.IsNullOrEmpty(mod.FileName))
                    {
                        manifestMods[mod.FileName] = mod;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Mods", $"Failed to read manifest: {ex.Message}");
            }
        }
        
        // Scan for mod files in the mods folder (supports .jar, .zip, .hmod, .litemod, .disabled)
        var modExtensions = new[] { "*.jar", "*.zip", "*.hmod", "*.litemod", "*.disabled" };
        var modFiles = modExtensions
            .SelectMany(ext => Directory.GetFiles(modsPath, ext, SearchOption.TopDirectoryOnly))
            .Distinct()
            .ToArray();
        var foundMods = new List<InstalledMod>();
        var needsManifestUpdate = false;
        
        foreach (var modPath in modFiles)
        {
            var fileName = Path.GetFileName(modPath);
            
            // Check if this mod file is already in manifest
            if (manifestMods.TryGetValue(fileName, out var existingMod))
            {
                foundMods.Add(existingMod);
            }
            else
            {
                // Mod file exists but not in manifest - add it as a local mod
                Logger.Info("Mods", $"Found untracked mod: {fileName}");
                var localMod = new InstalledMod
                {
                    Id = $"local-{Path.GetFileNameWithoutExtension(fileName)}",
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    FileName = fileName,
                    Enabled = true,
                    Version = "Local",
                    Author = "Local",
                    Description = "Manually installed mod file"
                };
                foundMods.Add(localMod);
                manifestMods[fileName] = localMod;
                needsManifestUpdate = true;
            }
        }
        
        // Update manifest if we found untracked mods
        if (needsManifestUpdate)
        {
            try
            {
                var manifestOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestMods.Values.ToList(), manifestOptions));
                Logger.Info("Mods", "Updated manifest with untracked mods");
            }
            catch (Exception ex)
            {
                Logger.Warning("Mods", $"Failed to update manifest: {ex.Message}");
            }
        }
        
        return foundMods;
    }

    public async Task<bool> InstallModFileToInstanceAsync(string modId, string fileId, string branch, int version)
    {
        try
        {
            // Get the target instance path and create mods directory inside UserData
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
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
            
            // Update manifest - use lock to prevent concurrent writes from corrupting data
            await _modManifestLock.WaitAsync();
            try
            {
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
                
                // Find and delete old version file if updating/downgrading
                var existingMod = installedMods.FirstOrDefault(m => m.Id == $"cf-{modId}" || m.CurseForgeId == modId);
                if (existingMod != null && !string.IsNullOrEmpty(existingMod.FileName) && existingMod.FileName != fileName)
                {
                    var oldFilePath = Path.Combine(modsPath, existingMod.FileName);
                    if (File.Exists(oldFilePath))
                    {
                        try
                        {
                            File.Delete(oldFilePath);
                            Logger.Info("Mods", $"Deleted old version: {existingMod.FileName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Mods", $"Failed to delete old mod file: {ex.Message}");
                        }
                    }
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
            }
            finally
            {
                _modManifestLock.Release();
            }
            
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
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
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
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
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

    public bool OpenInstanceFolder(string branch, int version)
    {
        try
        {
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
            Directory.CreateDirectory(versionPath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{versionPath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{versionPath}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start("xdg-open", $"\"{versionPath}\"");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Exports an instance (UserData folder) as a ZIP file to the Downloads folder.
    /// Returns the path to the exported file.
    /// </summary>
    public string? ExportInstance(string branch, int version)
    {
        try
        {
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            string versionPath = ResolveInstancePath(resolvedBranch, version, preferExisting: true);
            string userDataPath = Path.Combine(versionPath, "UserData");
            
            if (!Directory.Exists(userDataPath))
            {
                Logger.Warning("Instance", "No UserData folder to export");
                return null;
            }
            
            // Create export filename
            var versionLabel = version == 0 ? "latest" : $"v{version}";
            var exportFileName = $"HyPrism-{resolvedBranch}-{versionLabel}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var exportPath = Path.Combine(downloadsPath, exportFileName);
            
            // Create ZIP file
            Logger.Info("Instance", $"Exporting instance to: {exportPath}");
            
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
            
            System.IO.Compression.ZipFile.CreateFromDirectory(userDataPath, exportPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            
            Logger.Success("Instance", $"Exported instance: {exportFileName}");
            return exportPath;
        }
        catch (Exception ex)
        {
            Logger.Error("Instance", $"Failed to export instance: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Gets detailed information about all installed instances.
    /// </summary>
    public List<InstalledVersionInfo> GetInstalledVersionsDetailed()
    {
        var result = new List<InstalledVersionInfo>();
        
        foreach (var branchName in new[] { "release", "pre-release" })
        {
            var versions = GetInstalledVersionsForBranch(branchName);
            foreach (var version in versions)
            {
                try
                {
                    var versionPath = ResolveInstancePath(branchName, version, preferExisting: true);
                    var userDataPath = Path.Combine(versionPath, "UserData");
                    
                    long size = 0;
                    if (Directory.Exists(userDataPath))
                    {
                        size = GetDirectorySize(userDataPath);
                    }
                    
                    result.Add(new InstalledVersionInfo
                    {
                        Version = version,
                        Branch = branchName,
                        Path = versionPath,
                        UserDataSize = size,
                        HasUserData = Directory.Exists(userDataPath)
                    });
                }
                catch { }
            }
        }
        
        return result.OrderByDescending(v => v.Version).ToList();
    }
    
    private long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch { }
            }
        }
        catch { }
        return size;
    }

    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string branch, int version)
    {
        var modsWithUpdates = new List<InstalledMod>();
        
        try
        {
            string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
            var existingPath = FindExistingInstancePath(resolvedBranch, version);
            if (string.IsNullOrWhiteSpace(existingPath) || !Directory.Exists(existingPath))
            {
                Logger.Warning("Mods", $"Instance not found for update check: {resolvedBranch} v{version}");
                return modsWithUpdates;
            }
            
            string versionPath = existingPath;
            string modsPath = GetModsPath(versionPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            if (!File.Exists(manifestPath))
            {
                Logger.Info("Mods", "No manifest found for update check");
                return modsWithUpdates;
            }
            
            var manifestJson = File.ReadAllText(manifestPath);
            var installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson, JsonOptions) ?? new List<InstalledMod>();
            
            // Filter to only CurseForge mods
            var curseForgemods = installedMods.Where(m => !string.IsNullOrEmpty(m.CurseForgeId) && !string.IsNullOrEmpty(m.FileId)).ToList();
            
            if (curseForgemods.Count == 0)
            {
                Logger.Info("Mods", "No CurseForge mods found for update check");
                return modsWithUpdates;
            }
            
            Logger.Info("Mods", $"Checking updates for {curseForgemods.Count} CurseForge mods");
            
            foreach (var mod in curseForgemods)
            {
                try
                {
                    // Get mod details from CurseForge to find latest file
                    var url = $"https://api.curseforge.com/v1/mods/{mod.CurseForgeId}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("x-api-key", "$2a$10$1W4EvLWzLe4.RM1kcxW9n.vxmBPEYcg9dvpT4r5OAlkQk/.6jQE4e");
                    
                    using var response = await HttpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode) continue;
                    
                    var json = await response.Content.ReadAsStringAsync();
                    var modResponse = JsonSerializer.Deserialize<CurseForgeModResponse>(json, JsonOptions);
                    var modData = modResponse?.Data;
                    
                    if (modData?.LatestFiles == null || modData.LatestFiles.Count == 0) continue;
                    
                    // Find the latest file (highest file ID or most recent)
                    var latestFile = modData.LatestFiles
                        .OrderByDescending(f => f.FileDate)
                        .ThenByDescending(f => f.Id)
                        .FirstOrDefault();
                    
                    if (latestFile == null) continue;
                    
                    // Compare file IDs - if different, there's an update (or downgrade)
                    if (latestFile.Id.ToString() != mod.FileId)
                    {
                        // Check if it's actually newer (higher file ID = newer usually, but also check date)
                        if (int.TryParse(mod.FileId, out int currentFileId) && latestFile.Id > currentFileId)
                        {
                            Logger.Info("Mods", $"Update available for {mod.Name}: {mod.FileId} -> {latestFile.Id}");
                            
                            // Return an InstalledMod with the update info set
                            var modWithUpdate = new InstalledMod
                            {
                                Id = mod.Id,
                                Name = mod.Name,
                                Slug = mod.Slug,
                                Version = mod.Version,
                                FileId = mod.FileId,
                                FileName = mod.FileName,
                                Enabled = mod.Enabled,
                                Author = mod.Author,
                                Description = mod.Description,
                                IconUrl = mod.IconUrl,
                                CurseForgeId = mod.CurseForgeId,
                                FileDate = mod.FileDate,
                                Screenshots = mod.Screenshots,
                                LatestFileId = latestFile.Id.ToString(),
                                LatestVersion = latestFile.DisplayName ?? latestFile.FileName ?? "",
                            };
                            modsWithUpdates.Add(modWithUpdate);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Mods", $"Failed to check update for {mod.Name}: {ex.Message}");
                }
            }
            
            Logger.Info("Mods", $"Found {modsWithUpdates.Count} mods with updates available");
        }
        catch (Exception ex)
        {
            Logger.Error("Mods", $"Failed to check for mod updates: {ex.Message}");
        }
        
        return modsWithUpdates;
    }

    public Task<List<InstalledMod>> CheckInstanceModUpdates(string branch, int version)
    {
        return CheckInstanceModUpdatesAsync(branch, version);
    }

    // Discord Announcements - loaded from .env file
    private static string DiscordAnnouncementChannelId = "";
    private static string DiscordBotToken = "";

    private static void LoadEnvFile()
    {
        // Try to find .env in current directory or parent directories
        var currentDir = Directory.GetCurrentDirectory();
        var envPath = Path.Combine(currentDir, ".env");
        
        // Also check the executable directory
        var exeDir = AppContext.BaseDirectory;
        var exeEnvPath = Path.Combine(exeDir, ".env");
        
        var pathToUse = File.Exists(envPath) ? envPath : (File.Exists(exeEnvPath) ? exeEnvPath : null);
        
        if (pathToUse == null)
        {
            Logger.Info("Discord", ".env file not found - Discord announcements will be disabled");
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(pathToUse);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                    
                var eqIndex = line.IndexOf('=');
                if (eqIndex <= 0) continue;
                
                var key = line.Substring(0, eqIndex).Trim();
                var value = line.Substring(eqIndex + 1).Trim();
                
                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);
                else if (value.StartsWith("'") && value.EndsWith("'"))
                    value = value.Substring(1, value.Length - 2);
                
                switch (key)
                {
                    case "DISCORD_BOT_TOKEN":
                        DiscordBotToken = value;
                        Logger.Info("Discord", "Loaded bot token from .env");
                        break;
                    case "DISCORD_CHANNEL_ID":
                        DiscordAnnouncementChannelId = value;
                        Logger.Info("Discord", $"Loaded channel ID from .env: {value}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to load .env file: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches the latest announcement from a Discord channel.
    /// Returns null if no announcement found, if Discord API is not configured,
    /// if announcements are disabled, or if the message has been dismissed.
    /// </summary>
    public async Task<DiscordAnnouncement?> GetDiscordAnnouncementAsync()
    {
        // Check if announcements are enabled
        if (!_config.ShowDiscordAnnouncements)
        {
            Logger.Info("Discord", "Discord announcements disabled in settings");
            return null;
        }

        if (string.IsNullOrEmpty(DiscordBotToken) || string.IsNullOrEmpty(DiscordAnnouncementChannelId))
        {
            Logger.Info("Discord", "Discord announcements not configured - skipping");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, 
                $"https://discord.com/api/v10/channels/{DiscordAnnouncementChannelId}/messages?limit=1");
            request.Headers.Add("Authorization", $"Bot {DiscordBotToken}");
            request.Headers.Add("User-Agent", "HyPrism/2.0.3");

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("Discord", $"Failed to fetch announcements: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var messages = JsonSerializer.Deserialize<List<DiscordMessage>>(content);

            if (messages == null || messages.Count == 0)
            {
                return null;
            }

            var msg = messages[0];
            
            // Check if this announcement has been dismissed
            if (msg.Id != null && _config.DismissedAnnouncementIds.Contains(msg.Id))
            {
                Logger.Info("Discord", $"Announcement {msg.Id} already dismissed - skipping");
                return null;
            }

            // Check if the message has a ❌ reaction from us (means it should be hidden)
            // This is checked via the message reactions
            if (msg.Reactions != null)
            {
                foreach (var reaction in msg.Reactions)
                {
                    if (reaction.Emoji?.Name == "❌" && reaction.Me == true)
                    {
                        Logger.Info("Discord", $"Announcement {msg.Id} has ❌ reaction - skipping");
                        return null;
                    }
                }
            }
            
            // Extract image if present (from attachments or embeds)
            string? imageUrl = null;
            if (msg.Attachments?.Count > 0)
            {
                var imgAttachment = msg.Attachments.FirstOrDefault(a => 
                    a.ContentType?.StartsWith("image/") == true);
                imageUrl = imgAttachment?.Url;
            }
            
            // Get author's highest role color if available
            string? roleColor = null;
            string? roleName = null;
            if (msg.Member?.Roles?.Count > 0)
            {
                // We'd need to fetch roles from guild, for now just use first role ID
                // In practice, you might want to cache guild roles
            }

            return new DiscordAnnouncement
            {
                Id = msg.Id ?? "",
                Content = msg.Content ?? "",
                AuthorName = msg.Author?.GlobalName ?? msg.Author?.Username ?? "Unknown",
                AuthorAvatar = msg.Author?.Id != null && msg.Author?.Avatar != null
                    ? $"https://cdn.discordapp.com/avatars/{msg.Author.Id}/{msg.Author.Avatar}.png?size=64"
                    : null,
                AuthorRole = roleName,
                RoleColor = roleColor,
                ImageUrl = imageUrl,
                Timestamp = msg.Timestamp ?? DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Error fetching announcement: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// React to a Discord message with the specified emoji.
    /// Used to mark announcements as shown (✅) or hidden (❌) in Discord.
    /// </summary>
    public async Task<bool> ReactToAnnouncementAsync(string messageId, string emoji)
    {
        if (string.IsNullOrEmpty(DiscordBotToken) || string.IsNullOrEmpty(DiscordAnnouncementChannelId))
        {
            return false;
        }

        try
        {
            // URL encode the emoji for the API
            var encodedEmoji = Uri.EscapeDataString(emoji);
            var url = $"https://discord.com/api/v10/channels/{DiscordAnnouncementChannelId}/messages/{messageId}/reactions/{encodedEmoji}/@me";
            
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Add("Authorization", $"Bot {DiscordBotToken}");
            request.Headers.Add("User-Agent", "HyPrism/2.0.3");

            using var response = await HttpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Logger.Info("Discord", $"Added {emoji} reaction to message {messageId}");
                return true;
            }
            else
            {
                Logger.Warning("Discord", $"Failed to add reaction: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Error adding reaction: {ex.Message}");
            return false;
        }
    }

    public Task<DiscordAnnouncement?> GetDiscordAnnouncement()
    {
        return GetDiscordAnnouncementAsync();
    }

    /// <summary>
    /// Opens the HyPrism launcher data folder in the system file manager.
    /// </summary>
    public bool OpenLauncherFolder()
    {
        try
        {
            Logger.Info("Folder", $"Opening launcher folder: {_appDir}");
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
        catch (Exception ex)
        {
            Logger.Warning("Folder", $"Failed to open launcher folder: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes all HyPrism launcher data including config, cache, and instances.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public bool DeleteLauncherData()
    {
        try
        {
            Logger.Warning("Folder", $"Deleting all launcher data at: {_appDir}");
            
            // Don't delete if app is running from within the folder
            if (_appDir.Contains(AppContext.BaseDirectory))
            {
                Logger.Warning("Folder", "Cannot delete launcher data - app is running from within the folder");
                return false;
            }
            
            if (Directory.Exists(_appDir))
            {
                Directory.Delete(_appDir, true);
                Logger.Info("Folder", "Launcher data deleted successfully");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning("Folder", $"Failed to delete launcher data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the current launcher folder path.
    /// </summary>
    public string GetLauncherFolderPath()
    {
        return _appDir;
    }

    /// <summary>
    /// Browse for a folder using native OS dialog.
    /// Returns the selected folder path or null if cancelled.
    /// </summary>
    public async Task<string?> BrowseFolderAsync(string? initialPath = null)
    {
        try
        {
            var startPath = initialPath ?? _appDir;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use osascript with stdin to avoid shell quoting issues
                var script = $@"tell application ""Finder""
                    activate
                    set theFolder to choose folder with prompt ""Select Folder"" default location (POSIX file ""{startPath.Replace("\"", "\\\"")}"" as alias)
                    return POSIX path of theFolder
                end tell";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                // Write script to stdin to avoid shell escaping issues
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
                
                // User cancelled or error - try simpler approach without default location
                if (!string.IsNullOrEmpty(error) && error.Contains("-128"))
                {
                    // User cancelled
                    return null;
                }
                
                // Fallback: try without default location
                var fallbackScript = @"tell application ""Finder""
                    activate
                    set theFolder to choose folder with prompt ""Select Folder""
                    return POSIX path of theFolder
                end tell";
                
                var fallbackPsi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var fallbackProcess = Process.Start(fallbackPsi);
                if (fallbackProcess == null) return null;
                
                await fallbackProcess.StandardInput.WriteAsync(fallbackScript);
                fallbackProcess.StandardInput.Close();
                
                var fallbackOutput = await fallbackProcess.StandardOutput.ReadToEndAsync();
                await fallbackProcess.WaitForExitAsync();
                
                if (fallbackProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(fallbackOutput))
                {
                    return fallbackOutput.Trim();
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use PowerShell to show folder picker on Windows
                var script = $@"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; $dialog.SelectedPath = '{startPath.Replace("'", "''")}'; if ($dialog.ShowDialog() -eq 'OK') {{ $dialog.SelectedPath }}";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
            else
            {
                // Linux - use zenity if available
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--file-selection --directory --title=\"Select Folder\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Folder", $"Failed to browse folder: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Browse for mod files using native OS dialog.
    /// Returns array of selected file paths or empty array if cancelled.
    /// </summary>
    public async Task<string[]> BrowseModFilesAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use osascript for macOS file picker
                var script = @"tell application ""Finder""
                    activate
                    set theFiles to choose file with prompt ""Select Mod Files"" of type {""jar"", ""zip"", ""hmod"", ""litemod"", ""json""} with multiple selections allowed
                    set filePaths to """"
                    repeat with aFile in theFiles
                        set filePaths to filePaths & POSIX path of aFile & ""\n""
                    end repeat
                    return filePaths
                end tell";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return Array.Empty<string>();
                
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use PowerShell to show file picker on Windows with multiselect
                var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Mod Files (*.jar;*.zip;*.hmod;*.litemod;*.json)|*.jar;*.zip;*.hmod;*.litemod;*.json|All Files (*.*)|*.*'; $dialog.Multiselect = $true; $dialog.Title = 'Select Mod Files'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileNames -join ""`n"" }";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return Array.Empty<string>();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();
                }
            }
            else
            {
                // Linux - use zenity
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = "--file-selection --multiple --title=\"Select Mod Files\" --file-filter=\"Mod Files | *.jar *.zip *.hmod *.litemod *.json\" --separator=\"\\n\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return Array.Empty<string>();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();
                }
            }
            
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse files: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Triggers a test Discord announcement popup for developer testing.
    /// </summary>
    public DiscordAnnouncement? GetTestAnnouncement()
    {
        return new DiscordAnnouncement
        {
            Id = "test-announcement-" + DateTime.UtcNow.Ticks,
            AuthorName = "HyPrism Bot",
            AuthorAvatar = null,
            AuthorRole = "Developer",
            RoleColor = "#FFA845",
            Content = "🎉 This is a test announcement!\n\nThis is used to preview how Discord announcements will appear in the launcher. You can dismiss this by clicking the X button or disabling announcements.\n\n✨ Features:\n• Author info with avatar\n• Role colors\n• Images and attachments\n• Smooth animations",
            ImageUrl = null,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Sets the game language by copying translated language files to the game's language folder.
    /// Maps launcher locale codes to game locale codes and copies appropriate language files.
    /// </summary>
    /// <param name="languageCode">The launcher language code (e.g., "en", "es", "de", "fr")</param>
    /// <returns>True if language files were successfully copied, false otherwise</returns>
    public async Task<bool> SetGameLanguageAsync(string languageCode)
    {
        try
        {
            // Map launcher language codes to game language folder names
            var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "en-US" },
                { "es", "es-ES" },
                { "de", "de-DE" },
                { "fr", "fr-FR" },
                { "ja", "ja-JP" },
                { "ko", "ko-KR" },
                { "pt", "pt-BR" },
                { "ru", "ru-RU" },
                { "tr", "tr-TR" },
                { "uk", "uk-UA" },
                { "zh", "zh-CN" },
                { "be", "be-BY" }
            };

            // Get the game language code
            if (!languageMapping.TryGetValue(languageCode, out var gameLanguageCode))
            {
                Logger.Warning("Language", $"Unknown language code: {languageCode}, defaulting to en-US");
                gameLanguageCode = "en-US";
            }

            Logger.Info("Language", $"Setting game language to: {gameLanguageCode}");

            // Find the game language source directory
            // First check if running from published app (game-lang folder next to executable)
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
            
            string? sourceLangDir = null;
            
            // Check various possible locations
            var possibleLocations = new List<string>();
            
            if (!string.IsNullOrEmpty(exeDir))
            {
                possibleLocations.Add(Path.Combine(exeDir, "game-lang", gameLanguageCode));
                possibleLocations.Add(Path.Combine(exeDir, "..", "Resources", "game-lang", gameLanguageCode)); // macOS bundle
            }
            
            // Development location
            possibleLocations.Add(Path.Combine(AppContext.BaseDirectory, "game-lang", gameLanguageCode));
            possibleLocations.Add(Path.Combine(AppContext.BaseDirectory, "assets", "game-lang", gameLanguageCode));
            
            foreach (var loc in possibleLocations)
            {
                if (Directory.Exists(loc))
                {
                    sourceLangDir = loc;
                    break;
                }
            }

            if (sourceLangDir == null)
            {
                Logger.Warning("Language", $"Language files not found for {gameLanguageCode}. Checked: {string.Join(", ", possibleLocations)}");
                return false;
            }

            Logger.Info("Language", $"Found language files at: {sourceLangDir}");

            // Get all installed game versions and update their language files
            var branches = new[] { "release", "pre-release" };
            int copiedCount = 0;

            foreach (var branch in branches)
            {
                try
                {
                    var versions = await GetVersionListAsync(branch);
                    foreach (var version in versions)
                    {
                        var instancePath = GetInstancePath(branch, version);
                        var targetLangDir = Path.Combine(instancePath, "Client", "Data", "Shared", "language", gameLanguageCode);

                        if (!Directory.Exists(instancePath))
                            continue;

                        // Create target language directory
                        Directory.CreateDirectory(targetLangDir);

                        // Copy all language files
                        await CopyDirectoryRecursiveAsync(sourceLangDir, targetLangDir);
                        copiedCount++;
                        Logger.Info("Language", $"Copied language files to: {targetLangDir}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Language", $"Failed to update language for branch {branch}: {ex.Message}");
                }
            }

            // Also update "latest" instance if it exists
            foreach (var branch in branches)
            {
                try
                {
                    var latestPath = GetLatestInstancePath(branch);
                    if (Directory.Exists(latestPath))
                    {
                        var targetLangDir = Path.Combine(latestPath, "Client", "Data", "Shared", "language", gameLanguageCode);
                        Directory.CreateDirectory(targetLangDir);
                        await CopyDirectoryRecursiveAsync(sourceLangDir, targetLangDir);
                        copiedCount++;
                        Logger.Info("Language", $"Copied language files to latest: {targetLangDir}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Language", $"Failed to update latest language for branch {branch}: {ex.Message}");
                }
            }

            Logger.Info("Language", $"Successfully updated language files for {copiedCount} game instance(s)");
            return copiedCount > 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Language", $"Failed to set game language: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Recursively copies all files from source directory to target directory.
    /// </summary>
    private async Task CopyDirectoryRecursiveAsync(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var targetPath = Path.Combine(targetDir, fileName);
            await Task.Run(() => File.Copy(file, targetPath, overwrite: true));
        }

        // Copy subdirectories
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var targetSubDir = Path.Combine(targetDir, dirName);
            await CopyDirectoryRecursiveAsync(dir, targetSubDir);
        }
    }

    /// <summary>
    /// Gets the list of available game languages that have translation files.
    /// </summary>
    public List<string> GetAvailableGameLanguages()
    {
        var languages = new List<string>();
        
        // Check for game-lang folders
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
        
        var possibleBaseDirs = new List<string>();
        
        if (!string.IsNullOrEmpty(exeDir))
        {
            possibleBaseDirs.Add(Path.Combine(exeDir, "game-lang"));
            possibleBaseDirs.Add(Path.Combine(exeDir, "..", "Resources", "game-lang"));
        }
        
        possibleBaseDirs.Add(Path.Combine(AppContext.BaseDirectory, "game-lang"));
        possibleBaseDirs.Add(Path.Combine(AppContext.BaseDirectory, "assets", "game-lang"));
        
        foreach (var baseDir in possibleBaseDirs)
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var langCode = Path.GetFileName(dir);
                    if (!languages.Contains(langCode))
                    {
                        languages.Add(langCode);
                    }
                }
                break; // Use first found location
            }
        }
        
        return languages;
    }
}

// Models

// Discord API models
public class DiscordMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("content")]
    public string? Content { get; set; }
    
    [JsonPropertyName("author")]
    public DiscordAuthor? Author { get; set; }
    
    [JsonPropertyName("member")]
    public DiscordMember? Member { get; set; }
    
    [JsonPropertyName("attachments")]
    public List<DiscordAttachment>? Attachments { get; set; }
    
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
    
    [JsonPropertyName("reactions")]
    public List<DiscordReaction>? Reactions { get; set; }
}

public class DiscordReaction
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
    
    [JsonPropertyName("me")]
    public bool Me { get; set; }
    
    [JsonPropertyName("emoji")]
    public DiscordEmoji? Emoji { get; set; }
}

public class DiscordEmoji
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class DiscordAuthor
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    
    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }
    
    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public class DiscordMember
{
    [JsonPropertyName("roles")]
    public List<string>? Roles { get; set; }
    
    [JsonPropertyName("nick")]
    public string? Nick { get; set; }
}

public class DiscordAttachment
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}

/// <summary>
/// Announcement data fetched from Discord channel.
/// </summary>
public class DiscordAnnouncement
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string? AuthorAvatar { get; set; }
    public string? AuthorRole { get; set; }
    public string? RoleColor { get; set; }
    public string? ImageUrl { get; set; }
    public string Timestamp { get; set; } = "";
}

/// <summary>
/// Status of Rosetta 2 installation on macOS Apple Silicon.
/// </summary>
public class RosettaStatus
{
    public bool NeedsInstall { get; set; }
    public string Message { get; set; } = "";
    public string Command { get; set; } = "";
    public string? TutorialUrl { get; set; }
}

public class Config
{
    public string Version { get; set; } = "2.0.0";
    public string UUID { get; set; } = "";
    public string Nick { get; set; } = "Hyprism";
    public string VersionType { get; set; } = "release";
    public int SelectedVersion { get; set; } = 0;
    public string InstanceDirectory { get; set; } = "";
    public bool MusicEnabled { get; set; } = true;
    /// <summary>
    /// Launcher update channel: "release" for stable updates, "beta" for beta updates.
    /// Beta releases are named like "beta3-3.0.0" on GitHub.
    /// </summary>
    public string LauncherBranch { get; set; } = "release";
    /// <summary>
    /// If true, the launcher will close after successfully launching the game.
    /// </summary>
    public bool CloseAfterLaunch { get; set; } = false;
    /// <summary>
    /// If true, Discord announcements will be shown in the launcher.
    /// </summary>
    public bool ShowDiscordAnnouncements { get; set; } = true;
    /// <summary>
    /// List of Discord announcement IDs that have been dismissed by the user.
    /// </summary>
    public List<string> DismissedAnnouncementIds { get; set; } = new();
    /// <summary>
    /// If true, news will not be fetched or displayed.
    /// </summary>
    public bool DisableNews { get; set; } = false;
    /// <summary>
    /// Background mode: "slideshow" for rotating backgrounds, or a specific background filename.
    /// </summary>
    public string BackgroundMode { get; set; } = "slideshow";
    /// <summary>
    /// Custom launcher data directory. If set, overrides the default app data location.
    /// </summary>
    public string LauncherDataDirectory { get; set; } = "";
    /// <summary>
    /// Accent color for the UI (hex format, e.g., "#FFA845").
    /// </summary>
    public string AccentColor { get; set; } = "#FFA845";
    /// <summary>
    /// If true, game will run in online mode (requires authentication).
    /// If false, game runs in offline mode.
    /// </summary>
    public bool OnlineMode { get; set; } = true;
    /// <summary>
    /// Auth server domain for online mode (e.g., "sessions.sanasol.ws").
    /// </summary>
    public string AuthDomain { get; set; } = "sessions.sanasol.ws";
    /// <summary>
    /// Last directory used for mod export. Defaults to Desktop.
    /// </summary>
    public string LastExportPath { get; set; } = "";
    /// <summary>
    /// List of saved profiles (UUID, name pairs).
    /// </summary>
    public List<Profile> Profiles { get; set; } = new();
    /// <summary>
    /// Index of the currently active profile. -1 means no profile selected (use UUID/Nick directly).
    /// </summary>
    public int ActiveProfileIndex { get; set; } = -1;
    /// <summary>
    /// Whether the user has completed the initial onboarding flow.
    /// </summary>
    public bool HasCompletedOnboarding { get; set; } = false;
}

/// <summary>
/// A user profile with UUID and display name.
/// </summary>
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UUID { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cache for version information to avoid checking from version 1 every time.
/// </summary>
public class VersionCache
{
    public Dictionary<string, List<int>> KnownVersions { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.MinValue;
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

public class UpdateInfo
{
    public int OldVersion { get; set; }
    public int NewVersion { get; set; }
    public bool HasOldUserData { get; set; }
    public string Branch { get; set; } = "";
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
    /// <summary>
    /// The latest available file ID from CurseForge (for update checking).
    /// </summary>
    public string LatestFileId { get; set; } = "";
    /// <summary>
    /// The latest available version string from CurseForge (for update display).
    /// </summary>
    public string LatestVersion { get; set; } = "";
}

/// <summary>
/// Entry for mod list import/export
/// </summary>
public class ModListEntry
{
    public string? CurseForgeId { get; set; }
    public string? FileId { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
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

/// <summary>
/// Represents a cosmetic item from Assets.zip
/// </summary>
public class CosmeticItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Detailed information about an installed game instance.
/// </summary>
public class InstalledVersionInfo
{
    public int Version { get; set; }
    public string Branch { get; set; } = "";
    public string Path { get; set; } = "";
    public long UserDataSize { get; set; }
    public bool HasUserData { get; set; }
}