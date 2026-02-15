using System.Net.Http.Headers;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.User;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Exception thrown when Hytale API returns 401/403, indicating token needs refresh.
/// </summary>
internal class HytaleAuthExpiredException : Exception
{
    public HytaleAuthExpiredException(string message) : base(message) { }
}

/// <summary>
/// Version source for official Hytale servers.
/// Requires an authenticated Hytale account with a purchased game.
/// </summary>
/// <remarks>
/// Endpoint: https://account-data.hytale.com/patches/{os}/{arch}/{channel}/{from_build}
/// The official API returns patch steps with signed download URLs.
/// Automatically refreshes access token on auth errors.
/// </remarks>
public class HytaleVersionSource : IVersionSource
{
    private const string PatchesApiBaseUrl = "https://account-data.hytale.com/patches";
    private const string PatchesCacheFileName = "patches.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private const int MaxAuthRetries = 2;

    private readonly string _appDir;
    private readonly HttpClient _httpClient;
    private readonly HytaleAuthService _authService;
    private readonly IConfigService _configService;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    // In-memory cache: cacheKey -> (timestamp, response)
    private readonly Dictionary<string, (DateTime CachedAt, OfficialPatchesResponse Response)> _cache = new();

    public HytaleVersionSource(string appDir, HttpClient httpClient, HytaleAuthService authService, IConfigService configService)
    {
        _appDir = appDir;
        _httpClient = httpClient;
        _authService = authService;
        _configService = configService;
    }

    #region IVersionSource Implementation

    /// <inheritdoc/>
    public string SourceId => "hytale";

    /// <inheritdoc/>
    public VersionSourceType Type => VersionSourceType.Official;

    /// <inheritdoc/>
    /// <remarks>
    /// Checks if ANY official profile has a valid session (not just the active one).
    /// </remarks>
    public bool IsAvailable => HasAnyOfficialProfile();

    /// <summary>
    /// Checks if there's any official profile with a session file.
    /// </summary>
    private bool HasAnyOfficialProfile()
    {
        // Quick check: if current session exists, we're available
        if (_authService.CurrentSession != null)
            return true;

        // Check config for any official profiles
        var config = _configService.Configuration;
        if (config.Profiles == null || config.Profiles.Count == 0)
            return false;

        // Check if any profile is official and has session file
        foreach (var profile in config.Profiles.Where(p => p.IsOfficial))
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile, createIfMissing: false, migrateLegacyByName: true);
            var sessionPath = Path.Combine(profileDir, "hytale_session.json");
            if (File.Exists(sessionPath))
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public int Priority => 0; // Highest priority

    /// <inheritdoc/>
    /// <remarks>
    /// Official Hytale API with from_build=0 returns the LATEST full version as a complete .pwr.
    /// This means for downloading the latest version, we DON'T need patch chains.
    /// Patches (from_build=1+) are only needed for updating existing installations.
    /// </remarks>
    public bool IsDiffBasedBranch(string branch) => false; // from_build=0 gives full downloads

    /// <inheritdoc/>
    public async Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        // from_build=0 returns the LATEST version as a full .pwr (not a patch)
        var response = await GetPatchesAsync(os, arch, branch, 0, ct);
        if (response == null || response.Steps.Count == 0)
        {
            return new List<CachedVersionEntry>();
        }

        // from_build=0 returns only the latest full version
        // Take only the first (latest) entry - that's the downloadable full version
        var latestStep = response.Steps.OrderByDescending(s => s.To).FirstOrDefault();
        if (latestStep == null)
        {
            return new List<CachedVersionEntry>();
        }

        var entries = new List<CachedVersionEntry>
        {
            new CachedVersionEntry
            {
                Version = latestStep.To,
                FromVersion = 0, // Mark as full version for download purposes
                PwrUrl = latestStep.Pwr,
                PwrHeadUrl = latestStep.PwrHead,
                SigUrl = latestStep.Sig
            }
        };

        // Also cache patches for future update functionality (from_build=1)
        // This runs in background and doesn't block the version list
        _ = CachePatchesAsync(os, arch, branch, ct);

        return entries;
    }

    /// <summary>
    /// Caches patch information for future update operations.
    /// Called in background when fetching versions.
    /// </summary>
    private async Task CachePatchesAsync(string os, string arch, string branch, CancellationToken ct)
    {
        try
        {
            // from_build=1 returns the patch chain for updating from version 1 onwards
            var patches = await GetPatchesAsync(os, arch, branch, 1, ct);
            if (patches != null && patches.Steps.Count > 0)
            {
                await SavePatchesToFileAsync(os, arch, branch, patches.Steps, ct);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("HytaleSource", $"Background patch caching failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves patch steps to a separate patches.json file.
    /// </summary>
    private async Task SavePatchesToFileAsync(string os, string arch, string branch, List<OfficialPatchStep> steps, CancellationToken ct)
    {
        try
        {
            var cacheDir = Path.Combine(_appDir, "Cache", "Game");
            Directory.CreateDirectory(cacheDir);
            var patchesFile = Path.Combine(cacheDir, PatchesCacheFileName);

            // Load existing cache or create new
            PatchesCacheSnapshot cache;
            if (File.Exists(patchesFile))
            {
                var json = await File.ReadAllTextAsync(patchesFile, ct);
                cache = JsonSerializer.Deserialize<PatchesCacheSnapshot>(json) ?? new PatchesCacheSnapshot();
            }
            else
            {
                cache = new PatchesCacheSnapshot();
            }

            // Update cache metadata
            cache.FetchedAtUtc = DateTime.UtcNow;
            cache.Os = os;
            cache.Arch = arch;

            // Store patches for this branch
            var patchSteps = steps.Select(s => new CachedPatchStep
            {
                From = s.From,
                To = s.To,
                PwrUrl = s.Pwr,
                PwrHeadUrl = s.PwrHead,
                SigUrl = s.Sig
            }).ToList();

            cache.Patches[branch] = patchSteps;

            // Save to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonOut = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(patchesFile, jsonOut, ct);

            Logger.Debug("HytaleSource", $"Saved {patchSteps.Count} patch steps for {branch} to {patchesFile}");
        }
        catch (Exception ex)
        {
            Logger.Warning("HytaleSource", $"Failed to save patches to file: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(os, arch, branch, ct);
        var entry = versions.FirstOrDefault(v => v.Version == version);
        return entry?.PwrUrl;
    }

    /// <inheritdoc/>
    public async Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        // Use from_build=fromVersion to get patches FROM that version
        var patches = await GetPatchesAsync(os, arch, branch, fromVersion, ct);
        if (patches == null) return null;

        // Find the step that matches this transition
        var step = patches.Steps.FirstOrDefault(s => s.From == fromVersion && s.To == toVersion);
        return step?.Pwr;
    }

    /// <inheritdoc/>
    public Task PreloadAsync(CancellationToken ct = default)
    {
        // No preloading needed - we fetch on demand with caching
        return Task.CompletedTask;
    }

    #endregion

    #region Internal API Methods

    /// <summary>
    /// Fetches patches from the official Hytale API.
    /// Automatically retries with token refresh on auth errors.
    /// </summary>
    internal async Task<OfficialPatchesResponse?> GetPatchesAsync(
        string os, string arch, string branch, int fromBuild = 0, CancellationToken ct = default)
    {
        return await FetchWithTokenRefreshAsync(
            async (accessToken) => await FetchPatchesInternalAsync(os, arch, branch, fromBuild, accessToken, ct),
            ct);
    }

    /// <summary>
    /// Internal method that performs the actual patches fetch.
    /// </summary>
    private async Task<OfficialPatchesResponse?> FetchPatchesInternalAsync(
        string os, string arch, string branch, int fromBuild, string accessToken, CancellationToken ct)
    {
        string cacheKey = $"{os}:{arch}:{branch}:{fromBuild}";
        
        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            Logger.Debug("HytaleSource", $"Using cached patches for {cacheKey}");
            return cached.Response;
        }

        await _fetchLock.WaitAsync(ct);
        try
        {
            // Double-check cache after acquiring lock
            if (_cache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            {
                return cached.Response;
            }

            string url = $"{PatchesApiBaseUrl}/{os}/{arch}/{branch}/{fromBuild}";
            Logger.Info("HytaleSource", $"Fetching patches from {url}...");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.SendAsync(request, cts.Token);

            // Throw specific exception for auth errors so we can retry with refresh
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new HytaleAuthExpiredException($"Auth error: {response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                Logger.Warning("HytaleSource", $"Patches API returned {response.StatusCode}: {errorBody}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var patchesResponse = JsonSerializer.Deserialize<OfficialPatchesResponse>(json);

            if (patchesResponse != null)
            {
                _cache[cacheKey] = (DateTime.UtcNow, patchesResponse);
                Logger.Success("HytaleSource", $"Got {patchesResponse.Steps.Count} patch steps for {branch}");
            }

            return patchesResponse;
        }
        catch (HytaleAuthExpiredException)
        {
            throw; // Re-throw to trigger refresh
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning("HytaleSource", "Patches API request timed out");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("HytaleSource", $"Failed to fetch patches: {ex.Message}");
            return null;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Executes an API call with automatic token refresh on auth errors.
    /// Uses GetValidOfficialSessionAsync to get session from ANY official profile.
    /// </summary>
    /// <typeparam name="T">The return type of the API call.</typeparam>
    /// <param name="apiCall">Function that takes access token and returns result.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the API call, or default if all retries fail.</returns>
    private async Task<T?> FetchWithTokenRefreshAsync<T>(
        Func<string, Task<T?>> apiCall,
        CancellationToken ct = default) where T : class
    {
        for (int attempt = 0; attempt < MaxAuthRetries; attempt++)
        {
            // Get valid session from ANY official profile (not just the active one)
            var session = await _authService.GetValidOfficialSessionAsync();
            if (session == null)
            {
                Logger.Debug("HytaleSource", "No valid Hytale session available from any official profile");
                return default;
            }

            try
            {
                return await apiCall(session.AccessToken);
            }
            catch (HytaleAuthExpiredException ex)
            {
                if (attempt < MaxAuthRetries - 1)
                {
                    Logger.Warning("HytaleSource", $"Auth error ({ex.Message}), forcing token refresh (attempt {attempt + 1}/{MaxAuthRetries})...");
                    
                    // Force a token refresh on the current session
                    await _authService.ForceRefreshAsync();
                    
                    // Clear cache since old URLs may have expired signatures
                    ClearCache();
                }
                else
                {
                    Logger.Error("HytaleSource", $"Auth failed after {MaxAuthRetries} attempts, giving up");
                    return default;
                }
            }
        }

        return default;
    }

    /// <summary>
    /// Gets the access token from the current Hytale session.
    /// </summary>
    public string? GetAccessToken() => _authService.CurrentSession?.AccessToken;

    /// <summary>
    /// Clears the patches cache. Call after re-authentication.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        Logger.Info("HytaleSource", "Cache cleared");
    }

    #endregion
}
