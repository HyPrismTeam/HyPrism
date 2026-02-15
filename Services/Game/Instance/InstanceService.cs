using HyPrism.Services.Core.Infrastructure;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using HyPrism.Models;

namespace HyPrism.Services.Game.Instance;

/// <summary>
/// Manages game instance paths, versioning, and data organization.
/// Handles instance discovery, creation, and migration from legacy launcher versions.
/// </summary>
/// <remarks>
/// Instances are organized by branch (release/pre-release) and version number.
/// This service also handles user data directories and cosmetic skins.
/// </remarks>
public class InstanceService : IInstanceService
{
    private readonly string _appDir;
    
    // Config Service dependency
    private readonly ConfigService _configService;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceService"/> class.
    /// </summary>
    /// <param name="appDir">The application data directory path.</param>
    /// <param name="configService">The configuration service for accessing settings.</param>
    public InstanceService(string appDir, ConfigService configService)
    {
        _appDir = appDir;
        _configService = configService;
    } 

    /// <summary>
    /// Gets the current configuration from the config service.
    /// </summary>
    /// <returns>The current configuration object.</returns>
    private Config GetConfig() => _configService.Configuration;
    
    /// <summary>
    /// Persists the current configuration to disk.
    /// </summary>
    /// <param name="config">The configuration object (parameter kept for API compatibility).</param>
    private void SaveConfig(Config config) => _configService.SaveConfig();

    /// <inheritdoc/>
    public string GetInstanceRoot()
    {
        var config = GetConfig();
        var root = string.IsNullOrWhiteSpace(config.InstanceDirectory)
            ? Path.Combine(_appDir, "Instances")
            : config.InstanceDirectory;

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

    /// <summary>
    /// Get the path for a specific branch (release/pre-release).
    /// </summary>
    public string GetBranchPath(string branch)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        return Path.Combine(GetInstanceRoot(), normalizedBranch);
    }

    /// <summary>
    /// Get the UserData path for a specific instance version.
    /// </summary>
    public string GetInstanceUserDataPath(string versionPath)
    {
        return Path.Combine(versionPath, "UserData");
    }

    /// <summary>
    /// Resolve version to actual number. Returns 0 if not found.
    /// Checks in order: provided version > config.SelectedVersion > latest.json > local folders
    /// </summary>
    public int ResolveVersionOrLatest(string branch, int version)
    {
        var config = GetConfig();
        if (version > 0) return version;
        #pragma warning disable CS0618 // Backward compatibility: SelectedVersion and VersionType kept for migration
        if (config.SelectedVersion > 0) return config.SelectedVersion;

        var info = LoadLatestInfo(branch);
        if (info?.Version > 0) return info.Version;

        string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? config.VersionType : branch;
        #pragma warning restore CS0618
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

    /// <summary>
    /// Find existing instance path by branch and version.
    /// Checks multiple locations including legacy naming formats and GUID-named folders.
    /// </summary>
    public string? FindExistingInstancePath(string branch, int version)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        string versionSegment = version == 0 ? "latest" : version.ToString();

        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            var branchPath = Path.Combine(root, normalizedBranch);
            
            // New layout: search GUID-named folders by checking meta.json
            if (Directory.Exists(branchPath))
            {
                foreach (var instanceDir in Directory.GetDirectories(branchPath))
                {
                    var folderName = Path.GetFileName(instanceDir);
                    
                    // Check if it's a GUID-named folder
                    if (Guid.TryParse(folderName, out _))
                    {
                        var meta = GetInstanceMeta(instanceDir);
                        if (meta != null)
                        {
                            // For latest (version 0), check IsLatest flag
                            if (version == 0 && meta.IsLatest)
                            {
                                return instanceDir;
                            }
                            // For specific versions, check Version match
                            if (version > 0 && meta.Version == version && 
                                meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                            {
                                return instanceDir;
                            }
                        }
                    }
                    
                    // Legacy: "latest" folder name
                    if (version == 0 && folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        return instanceDir;
                    }
                    
                    // Legacy: version number as folder name
                    if (version > 0 && folderName == version.ToString())
                    {
                        return instanceDir;
                    }
                }
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

    /// <summary>
    /// Get all instance roots including legacy locations.
    /// </summary>
    public IEnumerable<string> GetInstanceRootsIncludingLegacy()
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

    /// <summary>
    /// Get path for latest instance symlink/info.
    /// </summary>
    public string GetLatestInstancePath(string branch)
    {
        return Path.Combine(GetBranchPath(branch), "latest");
    }

    /// <summary>
    /// Get path for latest.json file.
    /// </summary>
    public string GetLatestInfoPath(string branch)
    {
            return Path.Combine(GetBranchPath(branch), "latest.json");
    }

        private string GetLegacyLatestInfoPath(string branch)
        {
            return Path.Combine(GetLatestInstancePath(branch), "latest.json");
        }

    /// <summary>
    /// Load latest instance info from latest.json.
    /// </summary>
    public LatestInstanceInfo? LoadLatestInfo(string branch)
    {
        try
        {
                var path = GetLatestInfoPath(branch);
                if (!File.Exists(path))
                {
                    path = GetLegacyLatestInfoPath(branch);
                    if (!File.Exists(path)) return null;
                }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LatestInstanceInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save latest instance info to latest.json.
    /// </summary>
    public void SaveLatestInfo(string branch, int version)
    {
        try
        {
            Directory.CreateDirectory(GetBranchPath(branch));
            var info = new LatestInstanceInfo { Version = version, UpdatedAt = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            File.WriteAllText(GetLatestInfoPath(branch), json);
        }
        catch (Exception ex)
        {
            Logger.Error("Instance", $"Failed to save latest info: {ex.Message}");
        }
    }

    /// <summary>
    /// Migrate legacy data from old launcher versions.
    /// Merges config settings and copies instance directories.
    /// </summary>
    public void MigrateLegacyData()
    {
        try
        {
            var config = GetConfig();
            
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
                bool allowMerge = string.IsNullOrWhiteSpace(config.Nick)
                                  || string.Equals(config.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(config.Nick, "Player", StringComparison.OrdinalIgnoreCase);

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
                        config.Nick = legacyConfig.Nick;
                        updated = true;
                        Logger.Success("Migrate", $"Migrated nickname: {legacyConfig.Nick}");
                    }

                    if (string.IsNullOrWhiteSpace(config.UUID) && !string.IsNullOrWhiteSpace(legacyConfig.UUID))
                    {
                        config.UUID = legacyConfig.UUID;
                        updated = true;
                    }

                    if (string.IsNullOrWhiteSpace(config.InstanceDirectory) && !string.IsNullOrWhiteSpace(legacyConfig.InstanceDirectory))
                    {
                        config.InstanceDirectory = legacyConfig.InstanceDirectory;
                        updated = true;
                    }

                    #pragma warning disable CS0618 // Legacy migration: reading old config values
                    if (config.SelectedVersion == 0 && legacyConfig.SelectedVersion > 0)
                    {
                        config.SelectedVersion = legacyConfig.SelectedVersion;
                        updated = true;
                    }

                    if (string.IsNullOrWhiteSpace(config.VersionType) && !string.IsNullOrWhiteSpace(legacyConfig.VersionType))
                    {
                        config.VersionType = NormalizeVersionType(legacyConfig.VersionType);
                        updated = true;
                    }
                    #pragma warning restore CS0618
                }

                // Fallback: pick up a legacy uuid file if config lacked one
                if (string.IsNullOrWhiteSpace(config.UUID))
                {
                    var legacyUuid = LoadLegacyUuid(legacyRoot);
                    if (!string.IsNullOrWhiteSpace(legacyUuid))
                    {
                        config.UUID = legacyUuid;
                        updated = true;
                        Logger.Info("Migrate", "Recovered legacy UUID from legacy folder.");
                    }
                }

                if (updated)
                {
                    SaveConfig(config);
                    
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

    /// <summary>
    /// Migrate legacy instance folders to new structure.
    /// Handles both copy migration and in-place restructuring.
    /// </summary>
    public void MigrateLegacyInstances(string legacyInstanceRoot)
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
    public void RestructureLegacyFoldersInPlace(string instanceRoot)
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

    /// <summary>
    /// Load UUID from legacy uuid.txt/uuid.dat files.
    /// </summary>
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

    /// <summary>
    /// Safely copy directory recursively, preventing infinite loops.
    /// </summary>
    public static void SafeCopyDirectory(string sourceDir, string destDir)
    {
        // Use UtilityService implementation which now has infinite loop protection
        UtilityService.CopyDirectory(sourceDir, destDir, false);
    }

    /// <summary>
    /// Normalize version type: "prerelease" or "pre-release" -> "pre-release"
    /// </summary>
    public static string NormalizeVersionType(string versionType)
    {
        return UtilityService.NormalizeVersionType(versionType);
    }
    
    /// <summary>
    /// Checks if the game client executable exists at the specified version path.
    /// Tries multiple layouts: new layout (Client/...) and legacy layout (game/Client/...).
    /// </summary>
    public bool IsClientPresent(string versionPath)
    {
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
                Logger.Info("Version", $"Client found at {clientPath}");
                return true;
            }
        }

        Logger.Info("Version", $"Client not found in {versionPath}");
        return false;
    }

    /// <summary>
    /// Checks if game assets are present at the specified version path.
    /// </summary>
    public bool AreAssetsPresent(string versionPath)
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
    /// Gets the path to a specific instance version. Returns latest path if version is 0.
    /// Searches existing instances by branch/version using meta.json.
    /// If not found, returns a path for a new instance (but does not create it).
    /// </summary>
    public string GetInstancePath(string branch, int version)
    {
        if (version == 0)
        {
            return GetLatestInstancePath(branch);
        }
        
        string normalizedBranch = NormalizeVersionType(branch);
        var branchPath = Path.Combine(GetInstanceRoot(), normalizedBranch);
        
        // Search for existing instance with this branch/version in ID-named folders
        if (Directory.Exists(branchPath))
        {
            foreach (var instanceDir in Directory.GetDirectories(branchPath))
            {
                var folderName = Path.GetFileName(instanceDir);
                
                // Skip "latest" folder
                if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Check if this is a legacy version-named folder (for backward compatibility)
                if (folderName == version.ToString())
                    return instanceDir;
                
                // Check meta.json for matching version
                var meta = GetInstanceMeta(instanceDir);
                if (meta != null && meta.Version == version && 
                    meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return instanceDir;
                }
            }
        }
        
        // Not found - return path where a new instance would be created
        // This is used when creating a new instance; actual folder name will be ID
        return Path.Combine(branchPath, version.ToString());
    }

    /// <summary>
    /// Resolves the instance path, optionally preferring existing legacy paths.
    /// </summary>
    public string ResolveInstancePath(string branch, int version, bool preferExisting)
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
    
    #region Legacy Config Migration

    /// <summary>
    /// Gets the list of legacy installation root directories to search for migrations.
    /// </summary>
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

    /// <summary>
    /// Loads configuration from a JSON file at the specified path.
    /// </summary>
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

    /// <summary>
    /// Loads configuration from a TOML file at the specified path.
    /// </summary>
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
                        #pragma warning disable CS0618 // Legacy migration: parsing old config format
                        cfg.VersionType = NormalizeVersionType(val);
                        #pragma warning restore CS0618
                        break;
                    case "selectedversion":
                        #pragma warning disable CS0618 // Legacy migration: parsing old config format
                        if (int.TryParse(val, out var sel)) cfg.SelectedVersion = sel;
                        #pragma warning restore CS0618
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

    #endregion

    /// <summary>
    /// Deletes a game instance by branch and version number.
    /// Also removes latest.json for latest instances (version 0).
    /// </summary>
    public bool DeleteGame(string branch, int versionNumber)
    {
        try
        {
            string normalizedBranch = UtilityService.NormalizeVersionType(branch);
            string versionPath = ResolveInstancePath(normalizedBranch, versionNumber, true);
            
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

    /// <summary>
    /// Deletes a game instance by unique ID.
    /// </summary>
    public bool DeleteGameById(string instanceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            var info = FindInstanceById(instanceId);
            var versionPath = GetInstancePathById(instanceId);
            if (string.IsNullOrWhiteSpace(versionPath) || !Directory.Exists(versionPath))
            {
                return false;
            }

            Directory.Delete(versionPath, true);

            if (info?.Version == 0)
            {
                var infoPath = GetLatestInfoPath(info.Branch);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game by id: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Scan for all installed instances in the standard hierarchy.
    /// </summary>
    public List<InstalledInstance> GetInstalledInstances()
    {
        var branches = new[] { "release", "pre-release" };
        var results = new List<InstalledInstance>();
        var root = GetInstanceRoot();

        if (!Directory.Exists(root)) return results;

        foreach (var branch in branches)
        {
            var branchDir = Path.Combine(root, branch);
            if (!Directory.Exists(branchDir)) continue;

            try
            {
                var folders = Directory.GetDirectories(branchDir);
                foreach (var folder in folders)
                {
                    var dirName = Path.GetFileName(folder);
                    
                    // First try to load meta.json to get version and ID
                    string? customName = null;
                    string instanceId = "";
                    int version = -1;
                    bool isLatest = false;
                    var metaPath = Path.Combine(folder, "meta.json");
                    
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(metaPath);
                            var meta = JsonSerializer.Deserialize<InstanceMeta>(json, JsonOptions);
                            if (meta != null)
                            {
                                instanceId = meta.Id ?? "";
                                customName = meta.Name;
                                version = meta.Version;
                                isLatest = meta.IsLatest;
                            }
                        }
                        catch { }
                    }
                    
                    // If no meta.json, try to parse folder name
                    if (version < 0)
                    {
                        if (string.Equals(dirName, "latest", StringComparison.OrdinalIgnoreCase))
                        {
                            version = 0;
                            isLatest = true;
                        }
                        else if (int.TryParse(dirName, out var parsedVersion))
                        {
                            version = parsedVersion;
                        }
                        else if (Guid.TryParse(dirName, out _))
                        {
                            // GUID folder without meta.json - skip (shouldn't happen normally)
                            Logger.Warning("InstanceService", $"GUID folder without meta.json: {folder}");
                            continue;
                        }
                        else
                        {
                            // Unknown folder format, skip
                            continue;
                        }
                    }

                    var userDataPath = Path.Combine(folder, "UserData");
                    bool hasUserData = Directory.Exists(userDataPath);
                    long size = 0;
                    if (hasUserData)
                    {
                        try
                        {
                            size = new DirectoryInfo(userDataPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                        }
                        catch { }
                    }

                    long totalSize = 0;
                    try
                    {
                        totalSize = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                    }
                    catch { }

                    // Fallback: try legacy metadata.json if no meta.json was found
                    if (string.IsNullOrEmpty(instanceId))
                    {
                        var metadataPath = Path.Combine(folder, "metadata.json");
                        if (File.Exists(metadataPath))
                        {
                            try
                            {
                                var json = File.ReadAllText(metadataPath);
                                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                                metadata?.TryGetValue("customName", out customName);
                            }
                            catch { }
                        }
                    }
                    
                    // Generate ID if not found and persist it
                    if (string.IsNullOrEmpty(instanceId))
                    {
                        // Do not materialize placeholder folders (e.g. accidental latest/UserData only)
                        // as real instances unless game files are actually installed.
                        if (!IsClientPresent(folder))
                        {
                            Logger.Debug("InstanceService", $"Skipping non-installed placeholder folder: {folder}");
                            continue;
                        }

                        instanceId = Guid.NewGuid().ToString();
                        // Persist the generated ID to meta.json
                        try
                        {
                            var newMeta = new InstanceMeta
                            {
                                Id = instanceId,
                                Name = customName ?? "",
                                Branch = branch,
                                Version = version,
                                CreatedAt = DateTime.UtcNow,
                                IsLatest = isLatest
                            };
                            var json = JsonSerializer.Serialize(newMeta, JsonOptions);
                            File.WriteAllText(metaPath, json);
                            Logger.Debug("InstanceService", $"Generated and persisted ID for {branch}/{version}: {instanceId}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("InstanceService", $"Failed to persist generated ID: {ex.Message}");
                        }
                    }

                    // Perform deep validation
                    var validationResult = ValidateGameIntegrity(folder);

                    results.Add(new InstalledInstance
                    {
                        Id = instanceId,
                        Branch = branch,
                        Version = version,
                        Path = folder,
                        HasUserData = hasUserData,
                        UserDataSize = size,
                        TotalSize = totalSize,
                        IsValid = validationResult.Status == InstanceValidationStatus.Valid,
                        ValidationStatus = validationResult.Status,
                        ValidationDetails = validationResult.Details,
                        CustomName = customName
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("InstanceService", $"Error scanning branch {branch}: {ex.Message}");
            }
        }

        return results.OrderByDescending(x => x.Version).ToList();
    }

    /// <summary>
    /// Performs deep validation of a game instance, checking all critical components.
    /// Returns detailed information about what's present and what's missing.
    /// </summary>
    public (InstanceValidationStatus Status, InstanceValidationDetails Details) ValidateGameIntegrity(string folder)
    {
        var details = new InstanceValidationDetails();
        var missingComponents = new List<string>();
        
        try
        {
            // 1. Check if the folder exists at all
            if (!Directory.Exists(folder))
            {
                details.ErrorMessage = "Instance directory does not exist";
                return (InstanceValidationStatus.NotInstalled, details);
            }

            // 2. Check for the executable (most critical)
            details.HasExecutable = CheckExecutablePresent(folder);
            if (!details.HasExecutable)
            {
                missingComponents.Add("Game executable");
            }

            // 3. Check for assets folder
            details.HasAssets = CheckAssetsPresent(folder);
            if (!details.HasAssets)
            {
                missingComponents.Add("Game assets");
            }

            // 4. Check for libraries/dependencies
            details.HasLibraries = CheckLibrariesPresent(folder);
            if (!details.HasLibraries)
            {
                missingComponents.Add("Game libraries");
            }

            // 5. Check for essential config files
            details.HasConfig = CheckConfigPresent(folder);
            if (!details.HasConfig)
            {
                // Config missing is not critical, just a warning
            }

            details.MissingComponents = missingComponents;

            // Determine overall status based on what's present
            // If we have the executable, consider it valid - we can't reliably verify file integrity
            if (details.HasExecutable)
            {
                return (InstanceValidationStatus.Valid, details);
            }
            else if (!details.HasExecutable && !details.HasAssets && !details.HasLibraries)
            {
                // Nothing is there - not installed
                return (InstanceValidationStatus.NotInstalled, details);
            }
            else
            {
                // Has some files but no executable - corrupted
                details.ErrorMessage = "Game executable is missing or corrupted";
                return (InstanceValidationStatus.Corrupted, details);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Error validating instance {folder}: {ex.Message}");
            details.ErrorMessage = ex.Message;
            return (InstanceValidationStatus.Unknown, details);
        }
    }

    /// <summary>
    /// Checks if the game executable is present at the specified path.
    /// </summary>
    private bool CheckExecutablePresent(string folder)
    {
        string clientPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            clientPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            clientPath = Path.Combine(folder, "Client", "HytaleClient.exe");
        }
        else
        {
            clientPath = Path.Combine(folder, "Client", "HytaleClient");
        }
        return File.Exists(clientPath);
    }

    /// <summary>
    /// Checks if game assets are present and contain actual files.
    /// </summary>
    private bool CheckAssetsPresent(string folder)
    {
        string assetsPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Assets");
        }
        else
        {
            assetsPath = Path.Combine(folder, "Client", "Assets");
        }

        if (!Directory.Exists(assetsPath))
        {
            return false;
        }

        // Check that assets folder is not empty and contains expected subfolders
        try
        {
            var entries = Directory.GetFileSystemEntries(assetsPath);
            if (entries.Length == 0)
            {
                return false;
            }

            // Check for at least some expected asset folders/files
            // This is a basic sanity check
            return entries.Length >= 3; // Should have multiple folders
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if required libraries/dependencies are present.
    /// </summary>
    private bool CheckLibrariesPresent(string folder)
    {
        // On macOS, libraries are bundled in the app bundle
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var frameworksPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Frameworks");
            if (Directory.Exists(frameworksPath))
            {
                return Directory.EnumerateFileSystemEntries(frameworksPath).Any();
            }
            // Also check MonoBleedingEdge if using Mono runtime
            var monoPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "MonoBleedingEdge");
            return Directory.Exists(monoPath) && Directory.EnumerateFileSystemEntries(monoPath).Any();
        }
        
        // On Windows/Linux, check for typical library locations
        var clientFolder = Path.Combine(folder, "Client");
        if (!Directory.Exists(clientFolder))
        {
            return false;
        }

        // Check for DLLs on Windows or .so files on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check for any DLL files or Mono runtime
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasDlls = Directory.EnumerateFiles(clientFolder, "*.dll", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasDlls;
        }
        else
        {
            // Linux - check for .so files or Mono
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasSo = Directory.EnumerateFiles(clientFolder, "*.so*", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasSo;
        }
    }

    /// <summary>
    /// Checks if essential config files are present.
    /// </summary>
    private bool CheckConfigPresent(string folder)
    {
        // Check for common config files
        var configFiles = new[]
        {
            Path.Combine(folder, "Client", "boot.config"),
            Path.Combine(folder, "Client", "globalgamemanagers"),
            Path.Combine(folder, "Client", "level0"),
        };

        // On macOS, config is inside the app bundle
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dataPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Data");
            return Directory.Exists(dataPath);
        }

        // At least one config file should exist
        return configFiles.Any(File.Exists) || 
               Directory.Exists(Path.Combine(folder, "Client", "HytaleClient_Data"));
    }

    private bool CheckInstanceValidity(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return File.Exists(Path.Combine(folder, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return File.Exists(Path.Combine(folder, "Client", "HytaleClient.exe"));
        }
        else
        {
            return File.Exists(Path.Combine(folder, "Client", "HytaleClient"));
        }
    }

    public void SetInstanceCustomName(string branch, int version, string? customName)
    {
        // Use GetInstancePath which properly searches GUID-named folders
        var instancePath = GetInstancePath(branch, version);
        
        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceService", $"Instance not found: {branch}/{version}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, $"{branch}/{version}");
    }

    public void SetInstanceCustomNameById(string instanceId, string? customName)
    {
        var instancePath = GetInstancePathById(instanceId);
        
        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceService", $"Instance not found by ID: {instanceId}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, instanceId);
    }

    private void SetInstanceNameInternal(string instancePath, string? customName, string logIdentifier)
    {
        try
        {
            // Load or create meta.json
            var meta = GetInstanceMeta(instancePath);
            if (meta == null)
            {
                Logger.Warning("InstanceService", $"No meta.json found for instance: {logIdentifier}");
                return;
            }

            // Update existing meta's Name field
            meta.Name = string.IsNullOrWhiteSpace(customName) 
                ? (meta.IsLatest ? $"{meta.Branch} (Latest)" : $"{meta.Branch} v{meta.Version}")
                : customName;

            // Save meta.json
            SaveInstanceMeta(instancePath, meta);
            
            // Also update Config.Instances for quick lookup
            SyncInstancesWithConfig();
            
            Logger.Info("InstanceService", $"Updated instance name for {logIdentifier}: {meta.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Failed to save instance name: {ex.Message}");
        }
    }

    #region Instance Meta Management

    /// <inheritdoc/>
    public InstanceMeta? GetInstanceMeta(string instancePath)
    {
        var metaPath = Path.Combine(instancePath, "meta.json");
        if (!File.Exists(metaPath))
        {
            // Try legacy metadata.json
            var legacyPath = Path.Combine(instancePath, "metadata.json");
            if (File.Exists(legacyPath))
            {
                return MigrateLegacyMetadata(instancePath, legacyPath);
            }
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<InstanceMeta>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceService", $"Failed to load meta.json: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public void SaveInstanceMeta(string instancePath, InstanceMeta meta)
    {
        try
        {
            Directory.CreateDirectory(instancePath);
            var metaPath = Path.Combine(instancePath, "meta.json");
            var json = JsonSerializer.Serialize(meta, JsonOptions);
            File.WriteAllText(metaPath, json);
            Logger.Debug("InstanceService", $"Saved meta.json for instance {meta.Id}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Failed to save meta.json: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public InstanceMeta CreateInstanceMeta(string branch, int version, string? name = null, bool isLatest = false)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        
        // For "latest" instances, check if one already exists (only one latest per branch)
        if (isLatest)
        {
            var existingLatest = FindInstanceByBranchAndVersion(normalizedBranch, 0);
            if (existingLatest != null)
            {
                var existingPath = GetInstancePathById(existingLatest.Id);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var existingMeta = GetInstanceMeta(existingPath);
                    if (existingMeta != null && existingMeta.IsLatest)
                    {
                        Logger.Debug("InstanceService", $"Latest instance already exists for {branch}");
                        return existingMeta;
                    }
                }
            }
        }
        // For non-latest instances, we allow multiple instances of the same version
        // Each will have a unique ID and folder

        // For "latest" instances, use the standard latest path
        string instancePath;
        string instanceId = Guid.NewGuid().ToString();
        
        if (isLatest)
        {
            instancePath = GetLatestInstancePath(normalizedBranch);
        }
        else
        {
            // Create folder with ID as name (new structure)
            instancePath = CreateInstanceDirectory(normalizedBranch, instanceId);
        }

        // Check if meta already exists at path (edge case)
        var pathMeta = GetInstanceMeta(instancePath);
        if (pathMeta != null)
        {
            Logger.Debug("InstanceService", $"Instance meta already exists at path {instancePath}");
            return pathMeta;
        }

        // Create new meta with generated ID
        var meta = new InstanceMeta
        {
            Id = instanceId,
            Name = name ?? (isLatest ? $"{normalizedBranch} (Latest)" : $"{normalizedBranch} v{version}"),
            Branch = normalizedBranch,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            IsLatest = isLatest
        };

        // Save to disk
        SaveInstanceMeta(instancePath, meta);

        // Also add to Config.Instances for fallback
        var config = GetConfig();
        config.Instances ??= new List<InstanceInfo>();
        
        var existingInfo = config.Instances.FirstOrDefault(i => i.Id == meta.Id);
        if (existingInfo == null)
        {
            config.Instances.Add(new InstanceInfo
            {
                Id = meta.Id,
                Name = meta.Name,
                Branch = meta.Branch,
                Version = meta.Version
            });
            SaveConfig(config);
        }

        Logger.Info("InstanceService", $"Created instance meta: {meta.Id} ({meta.Name})");
        return meta;
    }

    /// <inheritdoc/>
    public InstanceInfo? GetSelectedInstance()
    {
        var config = GetConfig();
        if (string.IsNullOrEmpty(config.SelectedInstanceId))
            return null;

        var info = FindInstanceById(config.SelectedInstanceId);
        if (info == null)
            return null;

        // Check if the instance is actually installed (game files exist)
        // Use GetInstancePathById first (GUID-named folders), fall back to legacy search
        var instancePath = GetInstancePathById(info.Id);
        if (string.IsNullOrEmpty(instancePath))
        {
            instancePath = FindExistingInstancePath(info.Branch, info.Version);
        }
        
        if (!string.IsNullOrEmpty(instancePath))
        {
            var (status, _) = ValidateGameIntegrity(instancePath);
            info.IsInstalled = status == InstanceValidationStatus.Valid;
        }
        else
        {
            info.IsInstalled = false;
        }

        return info;
    }

    /// <inheritdoc/>
    public void SetSelectedInstance(string instanceId)
    {
        var config = GetConfig();
        config.Instances ??= new List<InstanceInfo>();

        var selected = FindInstanceById(instanceId);
        if (selected == null)
        {
            Logger.Warning("InstanceService", $"SetSelectedInstance ignored: instance not found ({instanceId})");
            return;
        }

        config.SelectedInstanceId = instanceId;

        // Keep legacy launch config in sync with selected instance so launch paths
        // that still read VersionType/SelectedVersion target the same instance.
        #pragma warning disable CS0618 // Backward compatibility: VersionType and SelectedVersion kept for migration
        config.VersionType = NormalizeVersionType(selected.Branch);
        config.SelectedVersion = selected.Version;
        #pragma warning restore CS0618

        SaveConfig(config);
        Logger.Info("InstanceService", $"Selected instance: {instanceId} ({selected.Branch} v{selected.Version})");
    }

    /// <inheritdoc/>
    public InstanceInfo? FindInstanceById(string instanceId)
    {
        var config = GetConfig();
        config.Instances ??= new List<InstanceInfo>();
        
        // First check Config.Instances
        var info = config.Instances.FirstOrDefault(i => i.Id == instanceId);
        if (info != null)
            return info;

        // If not in config, scan disk
        SyncInstancesWithConfig();
        return config.Instances.FirstOrDefault(i => i.Id == instanceId);
    }

    /// <inheritdoc/>
    public void SyncInstancesWithConfig()
    {
        var config = GetConfig();
        config.Instances ??= new List<InstanceInfo>();
        var discoveredById = new Dictionary<string, InstanceInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            if (!Directory.Exists(root)) continue;

            foreach (var branchDir in Directory.GetDirectories(root))
            {
                var branchName = Path.GetFileName(branchDir);
                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                {
                    var meta = GetInstanceMeta(instanceDir);
                    if (meta == null) continue;

                    if (string.IsNullOrWhiteSpace(meta.Id))
                    {
                        meta.Id = Guid.NewGuid().ToString();
                        SaveInstanceMeta(instanceDir, meta);
                        Logger.Warning("InstanceService", $"Recovered empty instance ID at {instanceDir}: generated {meta.Id}");
                    }

                    if (discoveredById.ContainsKey(meta.Id))
                    {
                        Logger.Warning("InstanceService", $"Duplicate instance ID detected during sync: {meta.Id}. Keeping first entry and skipping {instanceDir}");
                        continue;
                    }

                    discoveredById[meta.Id] = new InstanceInfo
                    {
                        Id = meta.Id,
                        Name = meta.Name,
                        Branch = meta.Branch,
                        Version = meta.Version
                    };
                }
            }
        }

        config.Instances = discoveredById.Values
            .OrderBy(i => i.Branch)
            .ThenByDescending(i => i.Version)
            .ToList();
        
        SaveConfig(config);
        Logger.Debug("InstanceService", $"Synced {config.Instances.Count} instances with config");
    }

    /// <summary>
    /// Migrates legacy metadata.json to new meta.json format.
    /// </summary>
    private InstanceMeta? MigrateLegacyMetadata(string instancePath, string legacyPath)
    {
        try
        {
            var json = File.ReadAllText(legacyPath);
            var legacyData = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            
            // Parse branch and version from path
            var dirName = Path.GetFileName(instancePath);
            var parentName = Path.GetFileName(Path.GetDirectoryName(instancePath) ?? "");
            
            int version = 0;
            bool isLatest = dirName.Equals("latest", StringComparison.OrdinalIgnoreCase);
            if (!isLatest && int.TryParse(dirName, out var parsedVersion))
            {
                version = parsedVersion;
            }

            var meta = new InstanceMeta
            {
                Id = Guid.NewGuid().ToString(),
                Name = legacyData?.GetValueOrDefault("customName") ?? (isLatest ? $"{parentName} (Latest)" : $"{parentName} v{version}"),
                Branch = parentName,
                Version = version,
                CreatedAt = DateTime.UtcNow,
                IsLatest = isLatest
            };

            // Save new format
            SaveInstanceMeta(instancePath, meta);

            // Delete legacy file
            try { File.Delete(legacyPath); } catch { }

            Logger.Info("InstanceService", $"Migrated legacy metadata to meta.json: {meta.Id}");
            return meta;
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceService", $"Failed to migrate legacy metadata: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public string? GetInstancePathById(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        // Search in all branch directories for the instance with this ID
        var root = GetInstanceRoot();
        if (!Directory.Exists(root))
            return null;

        foreach (var branchDir in Directory.GetDirectories(root))
        {
            foreach (var instanceDir in Directory.GetDirectories(branchDir))
            {
                // Check if folder name is the ID (new structure)
                var folderName = Path.GetFileName(instanceDir);
                if (folderName == instanceId)
                    return instanceDir;

                // Check meta.json for ID (compatibility with version-named folders)
                var meta = GetInstanceMeta(instanceDir);
                if (meta?.Id == instanceId)
                    return instanceDir;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public InstanceInfo? FindInstanceByBranchAndVersion(string branch, int version)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var config = GetConfig();
        config.Instances ??= new List<InstanceInfo>();

        // First check Config.Instances
        var info = config.Instances.FirstOrDefault(i => 
            i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && i.Version == version);
        if (info != null)
            return info;

        // If not found, scan disk
        var branchPath = GetBranchPath(normalizedBranch);
        if (!Directory.Exists(branchPath))
            return null;

        foreach (var instanceDir in Directory.GetDirectories(branchPath))
        {
            var meta = GetInstanceMeta(instanceDir);
            if (meta != null && meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && meta.Version == version)
            {
                return new InstanceInfo
                {
                    Id = meta.Id,
                    Name = meta.Name,
                    Branch = meta.Branch,
                    Version = meta.Version
                };
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public string CreateInstanceDirectory(string branch, string instanceId)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var path = Path.Combine(GetInstanceRoot(), normalizedBranch, instanceId);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc/>
    public void MigrateVersionFoldersToIdFolders()
    {
        try
        {
            Logger.Info("Migrate", "Starting version-to-ID folder migration...");
            var root = GetInstanceRoot();
            if (!Directory.Exists(root))
            {
                Logger.Info("Migrate", "No instance root directory found, skipping migration");
                return;
            }

            int migratedCount = 0;

            foreach (var branchDir in Directory.GetDirectories(root))
            {
                var branchName = Path.GetFileName(branchDir);
                // Skip non-branch folders
                if (!branchName.Equals("release", StringComparison.OrdinalIgnoreCase) &&
                    !branchName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                {
                    var folderName = Path.GetFileName(instanceDir);

                    // Skip if folder is already named as GUID (new structure)
                    if (Guid.TryParse(folderName, out _))
                    {
                        continue;
                    }

                    // Handle "latest" folder - also needs to be renamed to ID
                    if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        var latestMeta = GetInstanceMeta(instanceDir);
                        string latestId;

                        if (latestMeta != null && !string.IsNullOrEmpty(latestMeta.Id))
                        {
                            latestId = latestMeta.Id;
                            // Ensure IsLatest is set correctly
                            if (!latestMeta.IsLatest)
                            {
                                latestMeta.IsLatest = true;
                                latestMeta.Version = 0;
                                if (string.IsNullOrEmpty(latestMeta.Name))
                                    latestMeta.Name = $"{branchName} (Latest)";
                                SaveInstanceMeta(instanceDir, latestMeta);
                            }
                        }
                        else
                        {
                            // Create meta for latest
                            latestId = Guid.NewGuid().ToString();
                            var newLatestMeta = new InstanceMeta
                            {
                                Id = latestId,
                                Name = $"{branchName} (Latest)",
                                Branch = branchName,
                                Version = 0,
                                CreatedAt = DateTime.UtcNow,
                                IsLatest = true
                            };
                            SaveInstanceMeta(instanceDir, newLatestMeta);
                            Logger.Info("Migrate", $"Created meta.json for latest instance in {branchName}");
                        }

                        // Rename folder from "latest" to ID
                        var newLatestPath = Path.Combine(branchDir, latestId);
                        if (!Directory.Exists(newLatestPath))
                        {
                            try
                            {
                                Directory.Move(instanceDir, newLatestPath);
                                Logger.Success("Migrate", $"Migrated {branchName}/latest -> {branchName}/{latestId}");
                                migratedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Migrate", $"Failed to rename latest folder: {ex.Message}");
                            }
                        }
                        continue;
                    }

                    // Check if this is a version-named folder (numeric)
                    if (!int.TryParse(folderName, out var version))
                    {
                        // Not a version number, skip
                        continue;
                    }

                    // This is a version-named folder, need to migrate
                    var meta = GetInstanceMeta(instanceDir);
                    string instanceId;

                    if (meta != null && !string.IsNullOrEmpty(meta.Id))
                    {
                        instanceId = meta.Id;
                    }
                    else
                    {
                        // Create new meta with ID
                        instanceId = Guid.NewGuid().ToString();
                        meta = new InstanceMeta
                        {
                            Id = instanceId,
                            Name = $"{branchName} v{version}",
                            Branch = branchName,
                            Version = version,
                            CreatedAt = DateTime.UtcNow,
                            IsLatest = false
                        };
                        SaveInstanceMeta(instanceDir, meta);
                    }

                    // Rename folder from version to ID
                    var newPath = Path.Combine(branchDir, instanceId);
                    if (Directory.Exists(newPath))
                    {
                        Logger.Warning("Migrate", $"Target folder already exists: {newPath}, skipping {instanceDir}");
                        continue;
                    }

                    try
                    {
                        Directory.Move(instanceDir, newPath);
                        Logger.Success("Migrate", $"Migrated {branchName}/{version} -> {branchName}/{instanceId}");
                        migratedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Migrate", $"Failed to rename {instanceDir} to {newPath}: {ex.Message}");
                    }
                }
            }

            if (migratedCount > 0)
            {
                Logger.Success("Migrate", $"Migrated {migratedCount} instance folder(s) to ID-based naming");
                // Sync config with new folder structure
                SyncInstancesWithConfig();
            }
            else
            {
                Logger.Info("Migrate", "No version-named folders found to migrate");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to migrate version folders to ID folders: {ex.Message}");
        }
    }

    #endregion
}

