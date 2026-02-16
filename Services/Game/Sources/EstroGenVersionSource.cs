using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Cached mirror speed test result.
/// </summary>
public class MirrorSpeedTestResult
{
    public string MirrorId { get; set; } = "";
    public string MirrorUrl { get; set; } = "";
    public string MirrorName { get; set; } = "";
    public long PingMs { get; set; } = -1;
    /// <summary>
    /// Download speed in MB/s (megabytes per second).
    /// </summary>
    public double SpeedMBps { get; set; } = -1;
    public bool IsAvailable { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Version source for EstroGen community mirror (licdn.estrogen.cat).
/// Provides full game versions and patch files.
/// </summary>
/// <remarks>
/// The mirror stores full game versions at:
/// <c>https://licdn.estrogen.cat/hytale/patches/{os}/{arch}/{branch}/0/{version}.pwr</c>
/// 
/// And diff patches at:
/// <c>https://licdn.estrogen.cat/hytale/patches/{os}/{arch}/{branch}/{from}/{to}.pwr</c>
/// 
/// The "0" in the URL path represents from_build=0, meaning full standalone versions.
/// </remarks>
public partial class EstroGenVersionSource : IVersionSource
{
    private const string MirrorBaseUrl = "https://licdn.estrogen.cat/hytale/patches";
    private const string MirrorName = "EstroGen";
    private const long MinFileSizeBytes = 1_048_576; // 1 MB minimum
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SpeedTestCacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly SemaphoreSlim _speedTestLock = new(1, 1);

    // Cache: (os, arch, branch, fromBuild) → (timestamp, versions)
    private readonly Dictionary<string, (DateTime CachedAt, List<int> Versions)> _versionCache = new();
    
    // Speed test cache
    private MirrorSpeedTestResult? _speedTestResult;

    // Regex to parse nginx autoindex HTML for .pwr files with size
    // Matches: <a href="22.pwr">22.pwr</a>                14-Feb-2026 00:39         1629314978
    [GeneratedRegex(@"<a\s+href=""(\d+)\.pwr"">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PwrFileWithSizeRegex();

    public EstroGenVersionSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    #region IVersionSource Implementation

    /// <inheritdoc/>
    public string SourceId => "estrogen";

    /// <inheritdoc/>
    public VersionSourceType Type => VersionSourceType.Mirror;

    /// <inheritdoc/>
    public bool IsAvailable => true; // Mirror is always available to try

    /// <inheritdoc/>
    public int Priority => 100; // Lower priority than official

    /// <inheritdoc/>
    /// <remarks>
    /// EstroGen mirror stores full versions at /0/, so no diff-based patching needed
    /// for fresh installations. Patches are stored at /{from}/ but we prefer full downloads.
    /// </remarks>
    public bool IsDiffBasedBranch(string branch) => false;

    /// <inheritdoc/>
    public async Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        var versions = await GetAvailableVersionsAsync(os, arch, branch, fromBuild: 0, ct);
        
        return versions.Select(v => new CachedVersionEntry
        {
            Version = v,
            FromVersion = 0, // Full download
            PwrUrl = BuildDownloadUrl(os, arch, branch, 0, v),
            SigUrl = BuildSigUrl(os, arch, branch, 0, v)
        }).OrderByDescending(e => e.Version).ToList();
    }

    /// <inheritdoc/>
    public Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        // Full version download URL (from_build=0)
        return Task.FromResult<string?>(BuildDownloadUrl(os, arch, branch, 0, version));
    }

    /// <inheritdoc/>
    public Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        // Diff patch URL (from_build=fromVersion, to=toVersion)
        return Task.FromResult<string?>(BuildDownloadUrl(os, arch, branch, fromVersion, toVersion));
    }

    /// <inheritdoc/>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        // Preload common platform/branch combinations
        var os = UtilityService.GetOS();
        var arch = UtilityService.GetArch();
        
        await GetAvailableVersionsAsync(os, arch, "release", 0, ct);
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Fetches available versions by parsing the nginx autoindex page.
    /// </summary>
    private async Task<List<int>> GetAvailableVersionsAsync(
        string os, string arch, string branch, int fromBuild, CancellationToken ct)
    {
        string cacheKey = $"{os}:{arch}:{branch}:{fromBuild}";

        // Check cache
        if (_versionCache.TryGetValue(cacheKey, out var cached) && 
            DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            Logger.Debug("EstroGenSource", $"Using cached versions for {cacheKey}: {cached.Versions.Count} versions");
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

            var indexUrl = BuildIndexUrl(os, arch, branch, fromBuild);
            Logger.Info("EstroGenSource", $"Fetching version index from {indexUrl}...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _httpClient.GetAsync(indexUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("EstroGenSource", $"Mirror returned {response.StatusCode} for {indexUrl}");
                return _versionCache.TryGetValue(cacheKey, out cached) ? cached.Versions : new List<int>();
            }

            var html = await response.Content.ReadAsStringAsync(cts.Token);
            var versions = ParseVersionsFromHtml(html);

            if (versions.Count > 0)
            {
                _versionCache[cacheKey] = (DateTime.UtcNow, versions);
                Logger.Success("EstroGenSource", $"Found {versions.Count} versions for {branch}: [{string.Join(", ", versions.Take(5))}{(versions.Count > 5 ? "..." : "")}]");
            }
            else
            {
                Logger.Warning("EstroGenSource", $"No versions found at {indexUrl}");
            }

            return versions;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning("EstroGenSource", "Mirror request timed out");
            return _versionCache.TryGetValue(cacheKey, out var fallback) ? fallback.Versions : new List<int>();
        }
        catch (Exception ex)
        {
            Logger.Warning("EstroGenSource", $"Failed to fetch versions: {ex.Message}");
            return _versionCache.TryGetValue(cacheKey, out var fallback) ? fallback.Versions : new List<int>();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Parses version numbers from nginx autoindex HTML.
    /// Only includes versions where file size >= 1 MB (valid game files).
    /// </summary>
    private static List<int> ParseVersionsFromHtml(string html)
    {
        var versions = new List<int>();
        var matches = PwrFileWithSizeRegex().Matches(html);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 2 && 
                int.TryParse(match.Groups[1].Value, out int version) &&
                long.TryParse(match.Groups[2].Value, out long fileSize))
            {
                // Only include versions with files >= 1 MB
                if (fileSize >= MinFileSizeBytes)
                {
                    versions.Add(version);
                }
                else
                {
                    Logger.Debug("EstroGenSource", $"Skipping version {version}: file size {fileSize} bytes < 1 MB");
                }
            }
        }

        return versions.Distinct().OrderByDescending(v => v).ToList();
    }

    /// <summary>
    /// Builds the index URL for browsing available versions.
    /// </summary>
    private static string BuildIndexUrl(string os, string arch, string branch, int fromBuild)
    {
        // https://licdn.estrogen.cat/hytale/patches/{os}/{arch}/{branch}/{from_build}/
        return $"{MirrorBaseUrl}/{os}/{arch}/{branch}/{fromBuild}/";
    }

    /// <summary>
    /// Builds the download URL for a specific version.
    /// </summary>
    private static string BuildDownloadUrl(string os, string arch, string branch, int fromBuild, int version)
    {
        // https://licdn.estrogen.cat/hytale/patches/{os}/{arch}/{branch}/{from_build}/{version}.pwr
        return $"{MirrorBaseUrl}/{os}/{arch}/{branch}/{fromBuild}/{version}.pwr";
    }

    /// <summary>
    /// Builds the signature URL for a specific version.
    /// </summary>
    private static string BuildSigUrl(string os, string arch, string branch, int fromBuild, int version)
    {
        // https://licdn.estrogen.cat/hytale/patches/{os}/{arch}/{branch}/{from_build}/{version}.pwr.sig
        return $"{MirrorBaseUrl}/{os}/{arch}/{branch}/{fromBuild}/{version}.pwr.sig";
    }

    /// <summary>
    /// Clears the version cache.
    /// </summary>
    public void ClearCache()
    {
        _versionCache.Clear();
        _speedTestResult = null;
        Logger.Info("EstroGenSource", "Cache cleared");
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
            Logger.Debug("EstroGenSource", $"Using cached speed test: {cached.PingMs}ms, {cached.SpeedMBps:F2} MB/s");
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
                // Test ping (HEAD request to root)
                var pingStart = DateTime.UtcNow;
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(TimeSpan.FromSeconds(5));

                var pingResponse = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, MirrorBaseUrl), 
                    pingCts.Token);

                result.PingMs = (long)(DateTime.UtcNow - pingStart).TotalMilliseconds;
                result.IsAvailable = pingResponse.IsSuccessStatusCode;

                if (!result.IsAvailable)
                {
                    Logger.Warning("EstroGenSource", $"Mirror ping failed: {pingResponse.StatusCode}");
                    _speedTestResult = result;
                    return result;
                }

                // Use UtilityService for correct OS/arch
                var os = UtilityService.GetOS();
                var arch = UtilityService.GetArch();
                
                // Get a real file URL to test download speed
                var testUrl = await GetSpeedTestUrlAsync(os, arch, ct);
                
                if (string.IsNullOrEmpty(testUrl))
                {
                    Logger.Debug("EstroGenSource", "No version found for speed test, speed test incomplete");
                    _speedTestResult = result;
                    return result;
                }

                Logger.Debug("EstroGenSource", $"Speed testing with URL: {testUrl}");

                // Download portion of file (up to 10 MB) to measure speed - target ~5-6 seconds
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
                    
                    Logger.Debug("EstroGenSource", $"Downloaded {totalRead / 1024.0:F1} KB in {elapsed:F2}s");
                }
                else
                {
                    Logger.Warning("EstroGenSource", $"Speed test download failed: {response.StatusCode}");
                }

                Logger.Success("EstroGenSource", $"Speed test: {result.PingMs}ms ping, {result.SpeedMBps:F2} MB/s");
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("EstroGenSource", "Speed test timed out");
                result.IsAvailable = false;
            }
            catch (Exception ex)
            {
                Logger.Warning("EstroGenSource", $"Speed test failed: {ex.Message}");
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
            // Try pre-release branch first (usually has more versions)
            var versions = await GetAvailableVersionsAsync(os, arch, "pre-release", 0, ct);
            if (versions.Count > 0)
            {
                var version = versions[0]; // Latest version
                return $"{MirrorBaseUrl}/{os}/{arch}/pre-release/0/{version}.pwr";
            }
            
            // Fallback to release branch
            versions = await GetAvailableVersionsAsync(os, arch, "release", 0, ct);
            if (versions.Count > 0)
            {
                var version = versions[0];
                return $"{MirrorBaseUrl}/{os}/{arch}/release/0/{version}.pwr";
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("EstroGenSource", $"Failed to get speed test URL: {ex.Message}");
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
