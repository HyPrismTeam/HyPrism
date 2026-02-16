using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Version source for CobyLobby community mirror (cobylobbyht.store).
/// Provides full game versions using JSON API endpoints.
/// </summary>
/// <remarks>
/// API endpoints:
/// - GET /launcher/patches/{branch}/latest?os_name={os}&amp;arch={arch} - Get latest version
/// - GET /launcher/patches/{branch}/versions?os_name={os}&amp;arch={arch} - List versions  
/// - GET /launcher/patches/{os}/{arch}/{branch}/{prev}/{target}.pwr - Download patch
/// 
/// Using prev=0 always returns full standalone versions (not diff patches).
/// </remarks>
public class CobyLobbyVersionSource : IVersionSource
{
    private const string MirrorBaseUrl = "https://cobylobbyht.store";
    private const string MirrorName = "CobyLobby";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SpeedTestCacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly SemaphoreSlim _speedTestLock = new(1, 1);

    // Cache: (os, arch, branch) → (timestamp, versions)
    private readonly Dictionary<string, (DateTime CachedAt, List<int> Versions)> _versionCache = new();
    
    // Speed test cache
    private MirrorSpeedTestResult? _speedTestResult;

    public CobyLobbyVersionSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    #region IVersionSource Implementation

    /// <inheritdoc/>
    public string SourceId => "cobylobby";

    /// <inheritdoc/>
    public VersionSourceType Type => VersionSourceType.Mirror;

    /// <inheritdoc/>
    public bool IsAvailable => true; // Mirror is always available to try

    /// <inheritdoc/>
    public int Priority => 101; // Lower priority than EstroGen (100) and official (0)

    /// <inheritdoc/>
    /// <remarks>
    /// CobyLobby mirror stores full versions with prev=0, so no diff-based patching needed.
    /// </remarks>
    public bool IsDiffBasedBranch(string branch) => false;

    /// <inheritdoc/>
    public async Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        var versions = await GetAvailableVersionsAsync(os, arch, branch, ct);
        
        return versions.Select(v => new CachedVersionEntry
        {
            Version = v,
            FromVersion = 0, // Full download
            PwrUrl = BuildDownloadUrl(os, arch, branch, 0, v),
            SigUrl = null // CobyLobby doesn't provide signature files
        }).OrderByDescending(e => e.Version).ToList();
    }

    /// <inheritdoc/>
    public Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        // Full version download URL (prev=0)
        return Task.FromResult<string?>(BuildDownloadUrl(os, arch, branch, 0, version));
    }

    /// <inheritdoc/>
    public Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        // Diff patch URL (prev=fromVersion, target=toVersion)
        return Task.FromResult<string?>(BuildDownloadUrl(os, arch, branch, fromVersion, toVersion));
    }

    /// <inheritdoc/>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        // Preload common platform/branch combinations
        var os = UtilityService.GetOS();
        var arch = UtilityService.GetArch();
        
        await GetAvailableVersionsAsync(os, arch, "release", ct);
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Fetches available versions from the JSON API.
    /// </summary>
    private async Task<List<int>> GetAvailableVersionsAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        string cacheKey = $"{os}:{arch}:{branch}";

        // Check cache
        if (_versionCache.TryGetValue(cacheKey, out var cached) && 
            DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            Logger.Debug("CobyLobbySource", $"Using cached versions for {cacheKey}: {cached.Versions.Count} versions");
            return cached.Versions;
        }

        await _fetchLock.WaitAsync(ct);
        try
        {
            // Double-check cache after acquiring lock
            if (_versionCache.TryGetValue(cacheKey, out cached) && 
                DateTime.UtcNow - cached.CachedAt < CacheTtl)
            {
                return cached.Versions;
            }

            // Map branch name: pre-release -> prerelease
            var apiBranch = branch == "pre-release" ? "prerelease" : branch;
            var versionsUrl = $"{MirrorBaseUrl}/launcher/patches/{apiBranch}/versions?os_name={os}&arch={arch}";
            Logger.Info("CobyLobbySource", $"Fetching versions from {versionsUrl}...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _httpClient.GetAsync(versionsUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("CobyLobbySource", $"Mirror returned {response.StatusCode} for {versionsUrl}");
                return _versionCache.TryGetValue(cacheKey, out cached) ? cached.Versions : new List<int>();
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var versions = ParseVersionsFromJson(json);

            if (versions.Count > 0)
            {
                _versionCache[cacheKey] = (DateTime.UtcNow, versions);
                Logger.Success("CobyLobbySource", $"Found {versions.Count} versions for {branch}: [{string.Join(", ", versions.Take(5))}{(versions.Count > 5 ? "..." : "")}]");
            }
            else
            {
                // No versions for this branch is normal (e.g., prerelease may not be mirrored)
                Logger.Debug("CobyLobbySource", $"No versions available for {branch} on this mirror");
            }

            return versions;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning("CobyLobbySource", "Mirror request timed out");
            return _versionCache.TryGetValue(cacheKey, out var fallback) ? fallback.Versions : new List<int>();
        }
        catch (Exception ex)
        {
            Logger.Warning("CobyLobbySource", $"Failed to fetch versions: {ex.Message}");
            return _versionCache.TryGetValue(cacheKey, out var fallback) ? fallback.Versions : new List<int>();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Parses version numbers from JSON response.
    /// Expected formats:
    /// - { "items": [{"version": 8, ...}, ...] } - CobyLobby format
    /// - { "versions": [22, 21, 20, ...] }
    /// - [22, 21, 20, ...]
    /// </summary>
    private static List<int> ParseVersionsFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var versions = new List<int>();

            // Format 1: { "items": [{"version": 8, ...}, ...] } - CobyLobby actual format
            if (root.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && 
                        item.TryGetProperty("version", out var versionEl))
                    {
                        if (versionEl.TryGetInt32(out int version))
                        {
                            versions.Add(version);
                        }
                        else if (versionEl.ValueKind == JsonValueKind.String && 
                                 int.TryParse(versionEl.GetString(), out version))
                        {
                            versions.Add(version);
                        }
                    }
                }
                
                // Valid CobyLobby format - return result (may be empty if no versions available)
                return versions.Distinct().OrderByDescending(v => v).ToList();
            }

            // Format 2: Try to get versions array directly or from "versions"/"targets" property
            JsonElement versionsArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                versionsArray = root;
            }
            else if (root.TryGetProperty("versions", out versionsArray) && versionsArray.ValueKind == JsonValueKind.Array)
            {
                // Got it from property
            }
            else if (root.TryGetProperty("targets", out versionsArray) && versionsArray.ValueKind == JsonValueKind.Array)
            {
                // Alternative property name
            }
            else
            {
                Logger.Warning("CobyLobbySource", "Response format not recognized");
                return new List<int>();
            }

            foreach (var element in versionsArray.EnumerateArray())
            {
                if (element.TryGetInt32(out int version))
                {
                    versions.Add(version);
                }
                else if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out version))
                {
                    versions.Add(version);
                }
            }

            return versions.Distinct().OrderByDescending(v => v).ToList();
        }
        catch (JsonException ex)
        {
            Logger.Warning("CobyLobbySource", $"Failed to parse JSON: {ex.Message}");
            return new List<int>();
        }
    }

    /// <summary>
    /// Gets the latest version for a branch.
    /// </summary>
    public async Task<int?> GetLatestVersionAsync(string os, string arch, string branch, CancellationToken ct = default)
    {
        try
        {
            var apiBranch = branch == "pre-release" ? "prerelease" : branch;
            var url = $"{MirrorBaseUrl}/launcher/patches/{apiBranch}/latest?os_name={os}&arch={arch}";
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            
            // Try different property names
            if (doc.RootElement.TryGetProperty("version", out var versionEl) ||
                doc.RootElement.TryGetProperty("latest", out versionEl) ||
                doc.RootElement.TryGetProperty("target", out versionEl))
            {
                if (versionEl.TryGetInt32(out int version))
                    return version;
            }
            
            // If root is just a number
            if (doc.RootElement.ValueKind == JsonValueKind.Number && 
                doc.RootElement.TryGetInt32(out int rootVersion))
            {
                return rootVersion;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CobyLobbySource", $"Failed to get latest version: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// Builds the download URL for a specific version.
    /// </summary>
    private static string BuildDownloadUrl(string os, string arch, string branch, int prev, int target)
    {
        var apiBranch = branch == "pre-release" ? "prerelease" : branch;
        // https://cobylobbyht.store/launcher/patches/{os}/{arch}/{branch}/{prev}/{target}.pwr
        return $"{MirrorBaseUrl}/launcher/patches/{os}/{arch}/{apiBranch}/{prev}/{target}.pwr";
    }

    /// <summary>
    /// Clears the version cache.
    /// </summary>
    public void ClearCache()
    {
        _versionCache.Clear();
        _speedTestResult = null;
        Logger.Info("CobyLobbySource", "Cache cleared");
    }

    #endregion

    #region Speed Test

    /// <summary>
    /// Gets cached speed test result if still valid.
    /// </summary>
    public MirrorSpeedTestResult? GetCachedSpeedTest()
    {
        if (_speedTestResult == null)
            return null;

        if (DateTime.UtcNow - _speedTestResult.TestedAt > SpeedTestCacheTtl)
            return null;

        return _speedTestResult;
    }

    /// <summary>
    /// Tests mirror speed (ping and download speed).
    /// Downloads a portion of a real game file to measure speed accurately.
    /// </summary>
    public async Task<MirrorSpeedTestResult> TestSpeedAsync(CancellationToken ct = default)
    {
        // Return cached result if valid
        var cached = GetCachedSpeedTest();
        if (cached != null)
        {
            Logger.Debug("CobyLobbySource", $"Using cached speed test: {cached.PingMs}ms, {cached.SpeedMBps:F2} MB/s");
            return cached;
        }

        await _speedTestLock.WaitAsync(ct);
        try
        {
            // Double-check cache
            cached = GetCachedSpeedTest();
            if (cached != null)
                return cached;

            var result = new MirrorSpeedTestResult
            {
                MirrorId = SourceId,
                MirrorUrl = MirrorBaseUrl,
                MirrorName = MirrorName,
                TestedAt = DateTime.UtcNow
            };

            try
            {
                // Test ping (health endpoint)
                var pingStart = DateTime.UtcNow;
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(TimeSpan.FromSeconds(5));

                var pingResponse = await _httpClient.GetAsync($"{MirrorBaseUrl}/health", pingCts.Token);

                result.PingMs = (long)(DateTime.UtcNow - pingStart).TotalMilliseconds;
                result.IsAvailable = pingResponse.IsSuccessStatusCode;

                if (!result.IsAvailable)
                {
                    Logger.Warning("CobyLobbySource", $"Mirror ping failed: {pingResponse.StatusCode}");
                    _speedTestResult = result;
                    return result;
                }

                var os = UtilityService.GetOS();
                var arch = UtilityService.GetArch();
                
                // Get a real file URL to test download speed
                var testUrl = await GetSpeedTestUrlAsync(os, arch, ct);
                
                if (string.IsNullOrEmpty(testUrl))
                {
                    Logger.Debug("CobyLobbySource", "No version found for speed test, speed test incomplete");
                    _speedTestResult = result;
                    return result;
                }

                Logger.Debug("CobyLobbySource", $"Speed testing with URL: {testUrl}");

                // Download portion of file (up to 10 MB) to measure speed
                const int testSizeBytes = 10 * 1024 * 1024; // 10 MB
                var speedStart = DateTime.UtcNow;
                using var speedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                speedCts.CancelAfter(TimeSpan.FromSeconds(30));

                using var request = new HttpRequestMessage(HttpMethod.Get, testUrl);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, testSizeBytes - 1);
                
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, speedCts.Token);
                
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(speedCts.Token);
                    var buffer = new byte[81920]; // 80 KB buffer
                    long totalRead = 0;
                    int bytesRead;
                    
                    while ((bytesRead = await stream.ReadAsync(buffer, speedCts.Token)) > 0)
                    {
                        totalRead += bytesRead;
                        if (totalRead >= testSizeBytes) break;
                    }
                    
                    var elapsed = (DateTime.UtcNow - speedStart).TotalSeconds;
                    
                    if (elapsed > 0 && totalRead > 0)
                    {
                        // Speed in MB/s (megabytes per second)
                        result.SpeedMBps = (totalRead / 1_048_576.0) / elapsed;
                    }
                    
                    Logger.Debug("CobyLobbySource", $"Downloaded {totalRead / 1024.0:F1} KB in {elapsed:F2}s");
                }
                else
                {
                    Logger.Warning("CobyLobbySource", $"Speed test download failed: {response.StatusCode}");
                }

                Logger.Success("CobyLobbySource", $"Speed test: {result.PingMs}ms ping, {result.SpeedMBps:F2} MB/s");
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("CobyLobbySource", "Speed test timed out");
                result.IsAvailable = false;
            }
            catch (Exception ex)
            {
                Logger.Warning("CobyLobbySource", $"Speed test failed: {ex.Message}");
                result.IsAvailable = false;
            }

            _speedTestResult = result;
            return result;
        }
        finally
        {
            _speedTestLock.Release();
        }
    }

    /// <summary>
    /// Gets a URL to use for speed testing from available versions.
    /// </summary>
    private async Task<string?> GetSpeedTestUrlAsync(string os, string arch, CancellationToken ct)
    {
        try
        {
            // Try pre-release branch first
            var versions = await GetAvailableVersionsAsync(os, arch, "pre-release", ct);
            if (versions.Count > 0)
            {
                var version = versions[0]; // Latest version
                return BuildDownloadUrl(os, arch, "pre-release", 0, version);
            }
            
            // Fallback to release branch
            versions = await GetAvailableVersionsAsync(os, arch, "release", ct);
            if (versions.Count > 0)
            {
                var version = versions[0];
                return BuildDownloadUrl(os, arch, "release", 0, version);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CobyLobbySource", $"Failed to get speed test URL: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// Gets mirror info for display in UI.
    /// </summary>
    public MirrorSpeedTestResult GetMirrorInfo()
    {
        return _speedTestResult ?? new MirrorSpeedTestResult
        {
            MirrorId = SourceId,
            MirrorUrl = MirrorBaseUrl,
            MirrorName = MirrorName,
            IsAvailable = true,
            PingMs = -1,
            SpeedMBps = -1
        };
    }

    #endregion
}
