using System.Text.Json;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for managing game versions, checking updates, and version caching.
/// </summary>
public class VersionService : IVersionService
{
    private readonly string _appDir;
    private readonly HttpClient _httpClient;
    private readonly IConfigService _configService;

    public VersionService(string appDir, HttpClient httpClient, IConfigService configService)
    {
        _appDir = appDir;
        _httpClient = httpClient;
        _configService = configService;
    }

    /// <summary>
    /// Get list of available versions for a branch.
    /// Uses caching to avoid re-checking all versions every time.
    /// Checks versions in parallel batches for performance.
    /// </summary>
    public async Task<List<int>> GetVersionListAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var result = new List<int>();
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();

        var freshCache = TryLoadFreshVersionsCache(normalizedBranch, osName, arch, TimeSpan.FromMinutes(15));
        if (freshCache != null)
        {
            Logger.Info("Version", $"Using cached versions for {branch}");
            return freshCache;
        }

        const int batchSize = 20;
        const int maxConsecutiveFailures = 20;
        int batchStart = 0;
        int consecutiveFailures = 0;
        using var semaphore = new SemaphoreSlim(8);

        while (consecutiveFailures < maxConsecutiveFailures)
        {
            ct.ThrowIfCancellationRequested();

            var tasks = Enumerable.Range(batchStart, batchSize)
                .Select(async version =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        return await CheckVersionExistsAsync(osName, arch, normalizedBranch, version, ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            var batchResults = await Task.WhenAll(tasks);

            foreach (var (version, exists) in batchResults.OrderBy(r => r.version))
            {
                if (exists)
                {
                    result.Add(version);
                    consecutiveFailures = 0;
                }
                else
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= maxConsecutiveFailures) break;
                }
            }

            batchStart += batchSize;
        }

        result.Sort((a, b) => b.CompareTo(a)); // Sort descending (latest first)

        SaveVersionsCacheSnapshot(normalizedBranch, osName, arch, result);
        
        Logger.Info("Version", $"Found {result.Count} versions for {branch}: [{string.Join(", ", result)}]");
        return result;
    }

    public bool TryGetCachedVersions(string branch, TimeSpan maxAge, out List<int> versions)
    {
        versions = new List<int>();
        var normalizedBranch = NormalizeBranch(branch);
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();

        var cached = TryLoadFreshVersionsCache(normalizedBranch, osName, arch, maxAge);
        if (cached == null)
        {
            return false;
        }

        versions = cached;
        return versions.Count > 0;
    }

    /// <summary>
    /// Check if a specific version exists on the server.
    /// </summary>
    private async Task<(int version, bool exists)> CheckVersionExistsAsync(string os, string arch, string versionType, int version, CancellationToken ct = default)
    {
        try
        {
            string url = $"https://game-patches.hytale.com/patches/{os}/{arch}/{versionType}/0/{version}.pwr";
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return (version, response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException)
        {
            throw;
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

    // Utility methods
    private string NormalizeBranch(string branch)
    {
        return branch.ToLowerInvariant() switch
        {
            "release" => "release",
            "pre-release" => "pre-release",
            "prerelease" => "pre-release",
            "pre_release" => "pre-release",
            "beta" => "beta",
            "alpha" => "alpha",
            _ => "release"
        };
    }

    private sealed class VersionsCacheSnapshot
    {
        public DateTime FetchedAtUtc { get; set; }
        public string Os { get; set; } = "";
        public string Arch { get; set; } = "";
        public Dictionary<string, List<int>> Versions { get; set; } = new();
    }

    private string GetVersionsCacheSnapshotPath()
        => Path.Combine(_appDir, "Cache", "Game", "versions.json");

    private List<int>? TryLoadFreshVersionsCache(string branch, string osName, string arch, TimeSpan maxAge)
    {
        try
        {
            var path = GetVersionsCacheSnapshotPath();
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<VersionsCacheSnapshot>(json);
            if (snapshot == null) return null;

            if (!string.Equals(snapshot.Os, osName, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(snapshot.Arch, arch, StringComparison.OrdinalIgnoreCase)) return null;

            var age = DateTime.UtcNow - snapshot.FetchedAtUtc;
            if (age > maxAge) return null;

            if (snapshot.Versions.TryGetValue(branch, out var versions) && versions.Count > 0)
            {
                return new List<int>(versions);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to load versions cache snapshot: {ex.Message}");
        }

        return null;
    }

    private void SaveVersionsCacheSnapshot(string branch, string osName, string arch, List<int> versions)
    {
        try
        {
            var path = GetVersionsCacheSnapshotPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            VersionsCacheSnapshot snapshot;
            if (File.Exists(path))
            {
                var existingJson = File.ReadAllText(path);
                snapshot = JsonSerializer.Deserialize<VersionsCacheSnapshot>(existingJson) ?? new VersionsCacheSnapshot();
            }
            else
            {
                snapshot = new VersionsCacheSnapshot();
            }

            snapshot.FetchedAtUtc = DateTime.UtcNow;
            snapshot.Os = osName;
            snapshot.Arch = arch;
            snapshot.Versions[branch] = new List<int>(versions);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to save versions cache snapshot: {ex.Message}");
        }
    }
}

