using System.Text.Json;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for managing game versions, checking updates, and version caching.
/// </summary>
public class VersionService
{
    private readonly string _appDir;
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;

    public VersionService(string appDir, HttpClient httpClient, ConfigService configService)
    {
        _appDir = appDir;
        _httpClient = httpClient;
        _configService = configService;
    }

    /// <summary>
    /// Get list of available versions for a branch.
    /// Uses caching to avoid re-checking all versions every time.
    /// </summary>
    public async Task<List<int>> GetVersionListAsync(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var result = new List<int>();
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();

        // Load version cache
        var cache = LoadVersionCache();
        int startVersion = 1;
        
        // If we have cached versions for this branch, start from the highest known version + 1
        if (cache.KnownVersions.TryGetValue(normalizedBranch, out var knownVersions) && knownVersions.Count > 0)
        {
            startVersion = knownVersions.Max() + 1;
            result.AddRange(knownVersions);
        }

        // Check for new versions starting from startVersion
        int currentVersion = startVersion;
        // If we have no cache, start checking from 0 just in case
        if (cache.KnownVersions.Count == 0) currentVersion = 0;

        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 20; // Increased to skip potential gaps or missing early versions

        while (consecutiveFailures < maxConsecutiveFailures)
        {
            var (version, exists) = await CheckVersionExistsAsync(osName, arch, normalizedBranch, currentVersion);
            
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

        // Verify that all cached versions still exist (in parallel)
        if (knownVersions != null && knownVersions.Count > 0)
        {
            var verifyTasks = knownVersions
                .Where(v => v < startVersion)
                .Select(v => CheckVersionExistsAsync(osName, arch, normalizedBranch, v))
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

    /// <summary>
    /// Check if a specific version exists on the server.
    /// </summary>
    private async Task<(int version, bool exists)> CheckVersionExistsAsync(string os, string arch, string versionType, int version)
    {
        try
        {
            string url = $"https://game-patches.hytale.com/patches/{os}/{arch}/{versionType}/0/{version}.pwr";
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return (version, response.IsSuccessStatusCode);
        }
        catch
        {
            return (version, false);
        }
    }

    /// <summary>
    /// Check if latest instance needs an update.
    /// </summary>
    public async Task<bool> CheckLatestNeedsUpdateAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var versions = await GetVersionListAsync(normalizedBranch);
        if (versions.Count == 0) return false;

        var latest = versions[0];
        var latestPath = getLatestInstancePath();
        var info = loadLatestInfo(normalizedBranch);
        var baseOk = isClientPresent(latestPath);
        if (!baseOk) return true;
        if (info == null)
        {
            Logger.Info("Update", $"No latest.json found for {normalizedBranch}, assuming update may be needed");
            return true;
        }
        return info.Version != latest;
    }
    
    /// <summary>
    /// Gets the version status for the latest instance.
    /// Returns detailed status with installed and latest version numbers.
    /// </summary>
    public async Task<VersionStatus> GetLatestVersionStatusAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        try
        {
            var normalizedBranch = NormalizeBranch(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            
            if (versions.Count == 0)
            {
                return new VersionStatus { Status = "none", InstalledVersion = 0, LatestVersion = 0 };
            }
            
            var latestAvailable = versions[0];
            var latestPath = getLatestInstancePath();
            var info = loadLatestInfo(normalizedBranch);
            var baseOk = isClientPresent(latestPath);
            
            // Not installed
            if (!baseOk)
            {
                return new VersionStatus 
                { 
                    Status = "not_installed", 
                    InstalledVersion = 0, 
                    LatestVersion = latestAvailable 
                };
            }
            
            // No version info - assume update needed
            if (info == null)
            {
                return new VersionStatus 
                { 
                    Status = "update_available", 
                    InstalledVersion = 0, 
                    LatestVersion = latestAvailable 
                };
            }
            
            // Compare versions
            if (info.Version < latestAvailable)
            {
                return new VersionStatus 
                { 
                    Status = "update_available", 
                    InstalledVersion = info.Version, 
                    LatestVersion = latestAvailable 
                };
            }
            
            // Current version
            return new VersionStatus 
            { 
                Status = "current", 
                InstalledVersion = info.Version, 
                LatestVersion = latestAvailable 
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Version", $"Failed to get latest version status: {ex.Message}");
            return new VersionStatus { Status = "error", InstalledVersion = 0, LatestVersion = 0 };
        }
    }

    /// <summary>
    /// Get pending update information.
    /// </summary>
    public async Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        try
        {
            var normalizedBranch = NormalizeBranch(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return null;

            var latestVersion = versions[0];
            var latestPath = getLatestInstancePath();
            var info = loadLatestInfo(normalizedBranch);
            
            if (info == null || info.Version == latestVersion) return null;
            
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
    /// Get sequence of patches to apply for differential update.
    /// </summary>
    public List<int> GetPatchSequence(int fromVersion, int toVersion)
    {
        var patches = new List<int>();
        for (int v = fromVersion + 1; v <= toVersion; v++)
        {
            patches.Add(v);
        }
        return patches;
    }

    // Version cache management
    private string GetVersionCachePath() => Path.Combine(_appDir, "version_cache.json");

    private VersionCache LoadVersionCache()
    {
        try
        {
            var path = GetVersionCachePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cache = JsonSerializer.Deserialize<VersionCache>(json);
                if (cache != null) return cache;
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

    // Utility methods
    private string NormalizeBranch(string branch)
    {
        return branch.ToLowerInvariant() switch
        {
            "release" => "release",
            "beta" => "beta",
            "alpha" => "alpha",
            _ => "release"
        };
    }
}

