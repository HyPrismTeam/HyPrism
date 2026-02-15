using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Models;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Net;

namespace HyPrism.Services.Game.Mod;

/// <summary>
/// Manages game modifications including searching, installing, updating, and tracking.
/// Integrates with CurseForge API for mod discovery and downloading.
/// </summary>
public class ModService : IModService
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    // CurseForge API base URL
    private const string CfApiBaseUrl = "https://api.curseforge.com";
    
    // CF Website Base URL
    private const string CfBaseUrl = "https://www.curseforge.com";

    // Hytale game ID on CurseForge
    private const int HytaleGameId = 70216;

    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConfigService _configService;
    private readonly InstanceService _instanceService;
    private readonly ProgressNotificationService _progressNotificationService;
    
    /// <summary>
    /// Gets the CurseForge API key from configuration.
    /// </summary>
    private string CurseForgeApiKey => _configService.Configuration.CurseForgeKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModService"/> class.
    /// </summary>
    public ModService(
        HttpClient httpClient, 
        string appDir,
        ConfigService configService,
        InstanceService instanceService,
        ProgressNotificationService progressNotificationService)
    {
        _httpClient = httpClient;
        _appDir = appDir;
        _configService = configService;
        _instanceService = instanceService;
        _progressNotificationService = progressNotificationService;
    }
    
    /// <summary>
    /// Creates an HttpRequestMessage for CurseForge API with proper headers.
    /// </summary>
    private HttpRequestMessage CreateCurseForgeRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, $"{CfApiBaseUrl}{endpoint}");
        request.Headers.Add("x-api-key", CurseForgeApiKey);
        request.Headers.Add("Accept", "application/json");
        return request;
    }
    
    /// <summary>
    /// Validates that the CurseForge API key is available.
    /// </summary>
    private bool HasApiKey()
    {
        if (!string.IsNullOrEmpty(CurseForgeApiKey)) return true;
        Logger.Warning("ModService", "CurseForge API key is not configured");
        return false;
    }

    private async Task<CurseForgeFile?> ResolveCurseForgeFileAsync(string modId, string? fileId)
    {
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            var fileEndpoint = $"/v1/mods/{modId}/files/{fileId}";
            using var fileRequest = CreateCurseForgeRequest(HttpMethod.Get, fileEndpoint);
            using var fileResponse = await _httpClient.SendAsync(fileRequest);

            if (!fileResponse.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Get file info returned {fileResponse.StatusCode} for mod {modId} file {fileId}");
                return null;
            }

            var fileJson = await fileResponse.Content.ReadAsStringAsync();
            var cfFileResp = JsonSerializer.Deserialize<CurseForgeFileResponse>(fileJson, _jsonOptions);
            return cfFileResp?.Data;
        }

        var filesEndpoint = $"/v1/mods/{modId}/files?pageSize=1";
        using var filesRequest = CreateCurseForgeRequest(HttpMethod.Get, filesEndpoint);
        using var filesResponse = await _httpClient.SendAsync(filesRequest);

        if (!filesResponse.IsSuccessStatusCode)
        {
            Logger.Warning("ModService", $"Get latest mod file returned {filesResponse.StatusCode} for mod {modId}");
            return null;
        }

        var filesJson = await filesResponse.Content.ReadAsStringAsync();
        var filesResp = JsonSerializer.Deserialize<CurseForgeFilesResponse>(filesJson, _jsonOptions);
        return filesResp?.Data?.FirstOrDefault();
    }

    private static string? BuildEdgeCdnFallbackUrl(string fileId, string? fileName)
    {
        if (!int.TryParse(fileId, out var numericFileId) || numericFileId <= 0)
            return null;

        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var firstPart = numericFileId / 1000;
        var secondPart = numericFileId % 1000;
        var encodedFileName = Uri.EscapeDataString(fileName.Trim());
        return $"https://edge.forgecdn.net/files/{firstPart}/{secondPart}/{encodedFileName}";
    }

    private async Task<string?> ResolveDownloadUrlAsync(string modId, string fileId, string? directUrl, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(directUrl))
            return directUrl;

        var endpoint = $"/v1/mods/{modId}/files/{fileId}/download-url";
        using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var edgeFallbackOnError = BuildEdgeCdnFallbackUrl(fileId, fileName);
            if (!string.IsNullOrWhiteSpace(edgeFallbackOnError))
            {
                Logger.Info("ModService", $"Falling back to deterministic CDN URL for mod {modId} file {fileId}");
                return edgeFallbackOnError;
            }

            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var downloadUrlResp = JsonSerializer.Deserialize<CurseForgeDownloadUrlResponse>(json, _jsonOptions);
        if (!string.IsNullOrWhiteSpace(downloadUrlResp?.Data))
            return downloadUrlResp.Data;

        var edgeFallback = BuildEdgeCdnFallbackUrl(fileId, fileName);
        if (!string.IsNullOrWhiteSpace(edgeFallback))
        {
            Logger.Info("ModService", $"Download-url payload missing, using deterministic CDN URL for mod {modId} file {fileId}");
            return edgeFallback;
        }

        return null;
    }
    
    /// <inheritdoc/>
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        if (!HasApiKey())
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };

        try
        {
            var index = page * pageSize;
            var sortOrderStr = sortOrder == 0 ? "asc" : "desc";
            var endpoint = $"/v1/mods/search?gameId={HytaleGameId}" +
                           $"&index={index}&pageSize={pageSize}" +
                           $"&sortField={sortField}&sortOrder={sortOrderStr}";

            if (!string.IsNullOrWhiteSpace(query))
            {
                endpoint += $"&searchFilter={Uri.EscapeDataString(query)}";
            }
            
            if (categories is { Length: > 0 })
            {
                var catId = categories[0];
                if (int.TryParse(catId, out var categoryId) && categoryId > 0)
                    endpoint += $"&categoryId={categoryId}";
            }
            
            using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"CurseForge search returned {response.StatusCode}");
                return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
            }

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null)
                return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };

            var mods = cfResponse.Data.Select(MapToModInfo).ToList();
            
            return new ModSearchResult
            {
                Mods = mods,
                TotalCount = cfResponse.Pagination?.TotalCount ?? mods.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Search failed: {ex.Message}");
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
        }
    }

    /// <inheritdoc/>
    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        if (!HasApiKey())
            return GetFallbackCategories();
        
        try
        {
            var endpoint = $"/v1/categories?gameId={HytaleGameId}";
            using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Categories request returned {response.StatusCode}");
                return GetFallbackCategories();
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeCategoriesResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null || cfResponse.Data.Count == 0)
                return GetFallbackCategories();
            
            // Find the "Mods" class category dynamically (matching original repo)
            var modsClass = cfResponse.Data.FirstOrDefault(c => c.IsClass == true &&
                string.Equals(c.Name, "mods", StringComparison.OrdinalIgnoreCase));
            int modsClassId = modsClass?.Id ?? 0;
            
            var categories = new List<ModCategory>
            {
                new ModCategory { Id = 0, Name = "All Mods", Slug = "all" }
            };
            
            // Get subcategories under the Mods class
            var modCategories = cfResponse.Data
                .Where(c => c.ParentCategoryId == modsClassId && c.IsClass != true)
                .Select(c => new ModCategory
                {
                    Id = c.Id,
                    Name = c.Name ?? "",
                    Slug = c.Slug ?? ""
                })
                .OrderBy(c => c.Name)
                .ToList();
            
            // Fallback: if no subcategories found, return all non-class categories
            if (modCategories.Count == 0)
            {
                modCategories = cfResponse.Data
                    .Where(c => c.IsClass != true)
                    .Select(c => new ModCategory
                    {
                        Id = c.Id,
                        Name = c.Name ?? "",
                        Slug = c.Slug ?? ""
                    })
                    .OrderBy(c => c.Name)
                    .ToList();
            }
            
            categories.AddRange(modCategories);
            
            return categories;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Failed to load categories: {ex.Message}");
            return GetFallbackCategories();
        }
    }
    
    private static List<ModCategory> GetFallbackCategories()
    {
        return new List<ModCategory>
        {
            new ModCategory { Id = 0, Name = "All Mods", Slug = "all" },
            new ModCategory { Id = 2, Name = "World Gen", Slug = "world-gen" },
            new ModCategory { Id = 3, Name = "Magic", Slug = "magic" },
            new ModCategory { Id = 4, Name = "Tech", Slug = "tech" }
        };
    }

    /// <inheritdoc/>
    public async Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null)
    {
        if (!HasApiKey()) return false;

        try
        {
            var resolvedModId = await ResolveCurseForgeModIdAsync(slugOrId);

            var (cfFile, downloadUrl) = await ResolveCurseForgeFileForInstallAsync(resolvedModId, fileIdOrVersion);
            if (cfFile == null)
            {
                Logger.Warning("ModService", $"Install failed: could not resolve file for mod={resolvedModId} requested='{fileIdOrVersion}'");
                return false;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Logger.Warning("ModService", $"Install failed: missing download URL for mod={resolvedModId} file={cfFile.Id} requested='{fileIdOrVersion}'");
                return false;
            }
            
            onProgress?.Invoke("downloading", cfFile.FileName ?? "mod file");
            
            // Download the file to UserData/Mods folder (correct Hytale mod location)
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            Directory.CreateDirectory(modsPath);
            
            var filePath = Path.Combine(modsPath, cfFile.FileName ?? $"mod_{cfFile.Id}.jar");

            const int maxDownloadAttempts = 3;
            HttpResponseMessage? downloadResponse = null;
            for (int attempt = 1; attempt <= maxDownloadAttempts; attempt++)
            {
                try
                {
                    downloadResponse?.Dispose();
                    downloadResponse = await _httpClient.GetAsync(downloadUrl);

                    if ((int)downloadResponse.StatusCode == 429 || (int)downloadResponse.StatusCode >= 500)
                    {
                        if (attempt == maxDownloadAttempts)
                        {
                            Logger.Warning("ModService", $"Download returned {downloadResponse.StatusCode} for mod={resolvedModId} file={cfFile.Id}");
                            return false;
                        }

                        var delayMs = 600 * attempt;
                        if (downloadResponse.Headers.TryGetValues("Retry-After", out var values))
                        {
                            var raw = values.FirstOrDefault();
                            if (int.TryParse(raw, out var seconds) && seconds > 0)
                                delayMs = Math.Min(30000, seconds * 1000);
                        }

                        await Task.Delay(delayMs);
                        continue;
                    }

                    if (!downloadResponse.IsSuccessStatusCode)
                    {
                        Logger.Warning("ModService", $"Download returned {downloadResponse.StatusCode} for mod={resolvedModId} file={cfFile.Id}");
                        return false;
                    }

                    try
                    {
                        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                        await downloadResponse.Content.CopyToAsync(fs);
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* ignore */ }

                        if (attempt == maxDownloadAttempts)
                        {
                            Logger.Warning("ModService", $"Failed writing mod file '{filePath}': {ex.Message}");
                            return false;
                        }

                        await Task.Delay(600 * attempt);
                        continue;
                    }

                    break; // success
                }
                catch (TaskCanceledException)
                {
                    if (attempt == maxDownloadAttempts)
                    {
                        Logger.Warning("ModService", $"Download timed out for mod={resolvedModId} file={cfFile.Id}");
                        return false;
                    }
                    await Task.Delay(600 * attempt);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxDownloadAttempts)
                    {
                        Logger.Warning("ModService", $"Download failed for mod={resolvedModId} file={cfFile.Id}: {ex.Message}");
                        return false;
                    }
                    await Task.Delay(600 * attempt);
                }
            }

            downloadResponse?.Dispose();
            
            onProgress?.Invoke("installing", cfFile.FileName ?? "mod file");
            
            // Get the actual numeric mod ID from the file response
            var numericModId = cfFile.ModId > 0 ? cfFile.ModId.ToString() : resolvedModId;
            var resolvedFileId = cfFile.Id > 0 ? cfFile.Id.ToString() : string.Empty;
            // Also get mod info for the manifest
            CurseForgeMod? modInfo = null;
            try
            {
                // Use numeric ID for mod info request
                var modEndpoint = $"/v1/mods/{numericModId}";
                using var modResponse = await SendCurseForgeGetWithRetryAsync(modEndpoint, $"mod info mod={numericModId}");
                if (modResponse != null && modResponse.IsSuccessStatusCode)
                {
                    var modJson = await modResponse.Content.ReadAsStringAsync();
                    var modResp = JsonSerializer.Deserialize<CurseForgeModResponse>(modJson, _jsonOptions);
                    modInfo = modResp?.Data;
                }
                else if (modResponse != null)
                {
                    Logger.Warning("ModService", $"Get mod info returned {modResponse.StatusCode} for mod={numericModId}");
                }
            }
            catch { /* Non-critical */ }
            
            var installedMod = new InstalledMod
            {
                Id = $"cf-{numericModId}",
                Name = modInfo?.Name ?? cfFile.DisplayName ?? cfFile.FileName ?? "Unknown Mod",
                Slug = modInfo?.Slug ?? "",
                Version = ExtractVersion(cfFile.DisplayName, cfFile.FileName),
                FileId = resolvedFileId,
                FileName = cfFile.FileName ?? "",
                Enabled = true,
                Author = modInfo?.Authors?.FirstOrDefault()?.Name ?? "",
                Description = modInfo?.Summary ?? "",
                IconUrl = modInfo?.Logo?.ThumbnailUrl ?? modInfo?.Logo?.Url ?? "",
                CurseForgeId = numericModId,  // Always save numeric ID
                FileDate = cfFile.FileDate ?? "",
                ReleaseType = cfFile.ReleaseType,
                Screenshots = modInfo?.Screenshots?.Select(s => new CurseForgeScreenshot
                {
                    Id = s.Id,
                    Title = s.Title,
                    ThumbnailUrl = s.ThumbnailUrl,
                    Url = s.Url
                }).ToList() ?? new List<CurseForgeScreenshot>()
            };

            // Add to manifest atomically to avoid duplicates under concurrent installs.
            await _modManifestLock.WaitAsync();
            try
            {
                var mods = GetInstanceInstalledMods(instancePath);
                // Remove existing entry for this mod if any (check both numeric ID and old slug-based ID)
                mods.RemoveAll(m =>
                    m.CurseForgeId == numericModId ||
                    m.CurseForgeId == slugOrId ||
                    m.CurseForgeId == resolvedModId ||
                    m.Id == $"cf-{numericModId}" ||
                    m.Id == $"cf-{slugOrId}" ||
                    m.Id == $"cf-{resolvedModId}");

                mods.Add(installedMod);
                await SaveInstanceModsUnsafeAsync(instancePath, mods);
            }
            finally
            {
                _modManifestLock.Release();
            }
            
            onProgress?.Invoke("complete", cfFile.FileName ?? "mod file");
            Logger.Success("ModService", $"Installed mod {installedMod.Name} (ID: {numericModId}) to {instancePath}");
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install failed: {ex.Message}");
            return false;
        }
    }

    private async Task<(CurseForgeFile? File, string DownloadUrl)> ResolveCurseForgeFileForInstallAsync(string resolvedModId, string fileIdOrVersion)
    {
        if (string.IsNullOrWhiteSpace(resolvedModId))
            return (null, "");

        // 1) Try direct file lookup if it looks numeric
        CurseForgeFile? cfFile = null;
        if (int.TryParse(fileIdOrVersion, out var numericFileId) && numericFileId > 0)
        {
            var fileEndpoint = $"/v1/mods/{resolvedModId}/files/{numericFileId}";
            using var fileResponse = await SendCurseForgeGetWithRetryAsync(fileEndpoint, $"file info mod={resolvedModId} file={numericFileId}");

            if (fileResponse != null && fileResponse.IsSuccessStatusCode)
            {
                var fileJson = await fileResponse.Content.ReadAsStringAsync();
                var cfFileResp = JsonSerializer.Deserialize<CurseForgeFileResponse>(fileJson, _jsonOptions);
                cfFile = cfFileResp?.Data;
            }
            else if (fileResponse != null)
            {
                Logger.Warning("ModService", $"Get file info returned {fileResponse.StatusCode} for mod={resolvedModId} file={numericFileId}");
            }
        }

        // 2) Fallback: list files and resolve by fileId or version-ish match
        if (cfFile == null)
        {
            try
            {
                var endpoint = $"/v1/mods/{resolvedModId}/files?index=0&pageSize=50";
                using var response = await SendCurseForgeGetWithRetryAsync(endpoint, $"list files mod={resolvedModId}");
                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
                    var files = (cfResponse?.Data ?? new List<CurseForgeFile>())
                        .OrderByDescending(f => SafeParseDate(f.FileDate))
                        .ToList();

                    if (files.Count > 0)
                    {
                        if (int.TryParse(fileIdOrVersion, out var fId) && fId > 0)
                        {
                            cfFile = files.FirstOrDefault(f => f.Id == fId) ?? files[0];
                        }
                        else
                        {
                            var needle = (fileIdOrVersion ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(needle))
                            {
                                cfFile = files.FirstOrDefault(f =>
                                    (!string.IsNullOrWhiteSpace(f.DisplayName) && f.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(f.FileName) && f.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
                            }

                            cfFile ??= files[0];
                        }
                    }
                }
                else if (response != null)
                {
                    Logger.Warning("ModService", $"Fallback list files returned {response.StatusCode} for mod={resolvedModId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Fallback list files failed for mod={resolvedModId}: {ex.Message}");
            }
        }

        if (cfFile == null)
            return (null, "");

        // 3) Prefer embedded downloadUrl, otherwise ask CurseForge for a generated download URL.
        var downloadUrl = cfFile.DownloadUrl ?? "";
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            try
            {
                var endpoint = $"/v1/mods/{resolvedModId}/files/{cfFile.Id}/download-url";
                using var response = await SendCurseForgeGetWithRetryAsync(endpoint, $"download-url mod={resolvedModId} file={cfFile.Id}");
                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        downloadUrl = dataEl.GetString() ?? "";
                    }
                }
                else if (response != null)
                {
                    Logger.Warning("ModService", $"Get download-url returned {response.StatusCode} for mod={resolvedModId} file={cfFile.Id}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Get download-url failed for mod={resolvedModId} file={cfFile.Id}: {ex.Message}");
            }
        }

        // 4) Final fallback: ForgeCDN URL format (works for most CurseForge-hosted files)
        if (string.IsNullOrWhiteSpace(downloadUrl) && !string.IsNullOrWhiteSpace(cfFile.FileName) && cfFile.Id > 0)
        {
            try
            {
                var a = cfFile.Id / 1000;
                var b = cfFile.Id % 1000;
                var fileNameEscaped = Uri.EscapeDataString(cfFile.FileName);
                downloadUrl = $"https://edge.forgecdn.net/files/{a}/{b:D3}/{fileNameEscaped}";
            }
            catch
            {
                // ignore
            }
        }

        return (cfFile, downloadUrl);
    }

    private async Task<HttpResponseMessage?> SendCurseForgeGetWithRetryAsync(string endpoint, string context)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
                var response = await _httpClient.SendAsync(request);

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt == maxAttempts)
                        return response;

                    // Try to respect Retry-After when present.
                    var delayMs = 500 * attempt;
                    if (response.Headers.TryGetValues("Retry-After", out var values))
                    {
                        var raw = values.FirstOrDefault();
                        if (int.TryParse(raw, out var seconds) && seconds > 0)
                            delayMs = Math.Min(30000, seconds * 1000);
                    }

                    response.Dispose();
                    await Task.Delay(delayMs);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxAttempts)
                {
                    Logger.Warning("ModService", $"CurseForge request failed ({context}): {ex.Message}");
                    return null;
                }
                await Task.Delay(500 * attempt);
            }
            catch (TaskCanceledException)
            {
                if (attempt == maxAttempts)
                {
                    Logger.Warning("ModService", $"CurseForge request timed out ({context})");
                    return null;
                }
                await Task.Delay(500 * attempt);
            }
        }

        return null;
    }

    private static DateTime SafeParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
        return DateTime.TryParse(s, out var dt) ? dt : DateTime.MinValue;
    }

    private async Task<string> ResolveCurseForgeModIdAsync(string slugOrId)
    {
        if (string.IsNullOrWhiteSpace(slugOrId)) return slugOrId;

        // Already numeric
        if (int.TryParse(slugOrId, out _)) return slugOrId;

        // Strip common prefix
        if (slugOrId.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = slugOrId.Substring(3);
            if (int.TryParse(trimmed, out _)) return trimmed;
        }

        // Best-effort: search by slug/name and pick exact slug match when possible.
        try
        {
            var filter = Uri.EscapeDataString(slugOrId.Trim());
            var endpoint = $"/v1/mods/search?gameId={HytaleGameId}&searchFilter={filter}&pageSize=25";
            using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return slugOrId;

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, _jsonOptions);
            var mods = cfResponse?.Data ?? new List<CurseForgeMod>();
            var exact = mods.FirstOrDefault(m => string.Equals(m.Slug, slugOrId, StringComparison.OrdinalIgnoreCase));
            if (exact?.Id > 0) return exact.Id.ToString();

            var first = mods.FirstOrDefault(m => m.Id > 0);
            if (first?.Id > 0) return first.Id.ToString();
        }
        catch
        {
            // Ignore, fall back to original
        }

        return slugOrId;
    }

    /// <inheritdoc/>
    public List<InstalledMod> GetInstanceInstalledMods(string instancePath)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        var manifestPath = Path.Combine(modsPath, "manifest.json");
        var legacyManifestPath = Path.Combine(instancePath, "Client", "mods", "manifest.json");

        Directory.CreateDirectory(modsPath);

        List<InstalledMod> mods;
        
        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InstalledMod>();
            }
            else if (File.Exists(legacyManifestPath))
            {
                var json = File.ReadAllText(legacyManifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InstalledMod>();
            }
            else
            {
                mods = new List<InstalledMod>();
            }
        }
        catch
        {
            mods = new List<InstalledMod>();
        }

        // Ensure mods are discoverable even when manifest is missing or stale
        var diskFiles = Directory.EnumerateFiles(modsPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Where(name => !name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(name =>
                name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Enrich existing local entries from embedded mod manifest.json (so we don't rely on filename guessing).
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FileName))
                continue;

            var isLocalLike = mod.Id?.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true ||
                              string.Equals(mod.Author, "Local file", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(mod.Version, "local", StringComparison.OrdinalIgnoreCase);

            if (!isLocalLike)
                continue;

            try
            {
                var fullPath = Path.Combine(modsPath, mod.FileName);
                if (TryReadLocalModManifest(fullPath, out var mf))
                {
                    if (!string.IsNullOrWhiteSpace(mf.Name)) mod.Name = mf.Name;
                    if (!string.IsNullOrWhiteSpace(mf.Author)) mod.Author = mf.Author;
                    if (!string.IsNullOrWhiteSpace(mf.Version)) mod.Version = mf.Version;
                    if (!string.IsNullOrWhiteSpace(mf.Slug)) mod.Slug = mf.Slug;
                    if (!string.IsNullOrWhiteSpace(mf.CurseForgeId)) mod.CurseForgeId = mf.CurseForgeId;
                }

                if (string.IsNullOrWhiteSpace(mod.Version) || string.Equals(mod.Version, "local", StringComparison.OrdinalIgnoreCase))
                {
                    var stem = mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(mod.FileName))
                        : Path.GetFileNameWithoutExtension(mod.FileName);
                    var split = SplitLocalNameAndVersion(stem);
                    if (string.IsNullOrWhiteSpace(mod.Name) || mod.Name == stem)
                    {
                        if (!string.IsNullOrWhiteSpace(split.Name)) mod.Name = split.Name;
                    }
                    if (!string.IsNullOrWhiteSpace(split.Version)) mod.Version = split.Version;
                }
            }
            catch
            {
                // ignore; keep entry as-is
            }
        }

        static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        // Keep existing metadata in sync with files when names drift (e.g. .jar -> .disabled)
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FileName))
                continue;

            if (diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var baseStem = mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(mod.FileName)
                : Path.GetFileNameWithoutExtension(mod.FileName);

            var candidate = diskFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), baseStem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), baseStem, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        // Second pass: metadata-first matching by mod name/slug to avoid duplicate "local" rows
        foreach (var mod in mods)
        {
            if (!string.IsNullOrWhiteSpace(mod.FileName) &&
                diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var modNameKey = NormalizeName(mod.Name);
            var slugKey = NormalizeName(mod.Slug);

            var candidate = diskFiles.FirstOrDefault(file =>
            {
                var stem = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file))
                    : Path.GetFileNameWithoutExtension(file);

                var fileKey = NormalizeName(stem);
                if (string.IsNullOrEmpty(fileKey)) return false;

                var nameMatch = !string.IsNullOrEmpty(modNameKey) &&
                                (fileKey.StartsWith(modNameKey) || modNameKey.StartsWith(fileKey));
                var slugMatch = !string.IsNullOrEmpty(slugKey) &&
                                (fileKey.Contains(slugKey) || slugKey.Contains(fileKey));

                return nameMatch || slugMatch;
            });

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        foreach (var fileName in diskFiles)
        {
            if (mods.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled
                ? Path.GetFileNameWithoutExtension(fileName)
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));

            var split = SplitLocalNameAndVersion(displayName);

            var disabledOriginalExtension = "";
            if (!enabled)
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(stem).ToLowerInvariant();
                if (ext is ".jar" or ".zip")
                {
                    disabledOriginalExtension = ext;
                }
            }

            mods.Add(new InstalledMod
            {
                Id = $"local-{fileName}",
                Name = string.IsNullOrWhiteSpace(split.Name) ? displayName : split.Name,
                FileName = fileName,
                Enabled = enabled,
                Version = split.Version,
                Author = "Local file",
                DisabledOriginalExtension = disabledOriginalExtension
            });

            try
            {
                var fullPath = Path.Combine(modsPath, fileName);
                if (TryReadLocalModManifest(fullPath, out var mf))
                {
                    var local = mods.Last();
                    if (!string.IsNullOrWhiteSpace(mf.Name)) local.Name = mf.Name;
                    if (!string.IsNullOrWhiteSpace(mf.Author)) local.Author = mf.Author;
                    if (!string.IsNullOrWhiteSpace(mf.Version)) local.Version = mf.Version;
                    if (!string.IsNullOrWhiteSpace(mf.Slug)) local.Slug = mf.Slug;
                    if (!string.IsNullOrWhiteSpace(mf.CurseForgeId)) local.CurseForgeId = mf.CurseForgeId;
                }
            }
            catch
            {
                // Non-fatal: local mods still appear even if manifest can't be read.
            }
        }

        // Final pass: if a synthetic local entry and a metadata-backed entry represent the same mod,
        // keep only the metadata-backed entry.
        bool IsSyntheticLocal(InstalledMod mod)
        {
            var isLocalId = mod.Id?.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true;
            var isLocalAuthor = string.Equals(mod.Author, "Local file", StringComparison.OrdinalIgnoreCase);
            return isLocalId || isLocalAuthor;
        }

        var metadataMods = mods.Where(m => !IsSyntheticLocal(m)).ToList();
        mods = mods
            .Where(local =>
            {
                if (!IsSyntheticLocal(local)) return true;

                var localNameKey = NormalizeName(local.Name);
                var localFileStem = local.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(local.FileName))
                    : Path.GetFileNameWithoutExtension(local.FileName ?? "");
                var localFileKey = NormalizeName(localFileStem ?? "");

                var hasMetadataTwin = metadataMods.Any(meta =>
                {
                    var metaNameKey = NormalizeName(meta.Name);
                    var metaSlugKey = NormalizeName(meta.Slug);
                    var metaFileStem = meta.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta.FileName))
                        : Path.GetFileNameWithoutExtension(meta.FileName ?? "");
                    var metaFileKey = NormalizeName(metaFileStem ?? "");

                    if (!string.IsNullOrEmpty(local.FileName) && !string.IsNullOrEmpty(meta.FileName) &&
                        string.Equals(local.FileName, meta.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localNameKey) &&
                        ((!string.IsNullOrEmpty(metaNameKey) && localNameKey == metaNameKey) ||
                         (!string.IsNullOrEmpty(metaSlugKey) && localNameKey == metaSlugKey)))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localFileKey) &&
                        ((!string.IsNullOrEmpty(metaFileKey) && localFileKey == metaFileKey)))
                    {
                        return true;
                    }

                    return false;
                });

                return !hasMetadataTwin;
            })
            .ToList();

        return mods;
    }

    private sealed class LocalModManifest
    {
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public string Version { get; init; } = "";
        public string Slug { get; init; } = "";
        public string CurseForgeId { get; init; } = "";
    }

    private static bool TryReadLocalModManifest(string archivePath, out LocalModManifest manifest)
    {
        manifest = new LocalModManifest();

        try
        {
            if (!File.Exists(archivePath))
                return false;

            // Many mods are .jar (zip) or .zip; some may be renamed to .disabled.
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries
                .Where(e =>
                    e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith("\\manifest.json", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (entries.Count == 0)
                return false;

            static string GetString(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                {
                    foreach (var p in obj.EnumerateObject())
                    {
                        if (!p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString() ?? "";
                        if (p.Value.ValueKind == JsonValueKind.Number) return p.Value.ToString();
                        return "";
                    }
                }
                return "";
            }

            static string GetAuthor(JsonElement obj)
            {
                foreach (var p in obj.EnumerateObject())
                {
                    if (p.Name.Equals("Author", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString() ?? "";

                    if (p.Name.Equals("Authors", StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Value.ValueKind == JsonValueKind.String)
                            return p.Value.GetString() ?? "";

                        if (p.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in p.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                    return item.GetString() ?? "";
                                if (item.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var ap in item.EnumerateObject())
                                    {
                                        if (ap.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) && ap.Value.ValueKind == JsonValueKind.String)
                                            return ap.Value.GetString() ?? "";
                                    }
                                }
                            }
                        }
                    }
                }

                return "";
            }

            static string GetFirstIdLike(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                {
                    foreach (var p in obj.EnumerateObject())
                    {
                        if (!p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var idNum))
                            return idNum.ToString();
                        if (p.Value.ValueKind == JsonValueKind.String)
                            return p.Value.GetString() ?? "";
                    }
                }
                return "";
            }

            static IEnumerable<JsonElement> CandidateObjects(JsonElement root)
            {
                yield return root;

                foreach (var p in root.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    if (p.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("info", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return p.Value;
                    }
                }
            }

            LocalModManifest? bestManifest = null;
            var bestScore = 0;

            foreach (var entry in entries)
            {
                try
                {
                    using var stream = entry.Open();
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var obj in CandidateObjects(doc.RootElement))
                    {
                        var name = GetString(obj, "Name", "ModName", "Title", "DisplayName", "name", "title");
                        var author = GetAuthor(obj);
                        if (string.IsNullOrWhiteSpace(author))
                            author = GetString(obj, "Publisher", "publisher");
                        var version = GetString(obj, "Version", "ModVersion", "DisplayVersion", "version");
                        var slug = GetString(obj, "Slug", "ModSlug", "slug");
                        var cfId = GetFirstIdLike(obj,
                            "CurseForgeId", "CurseForgeID", "CurseforgeId",
                            "CurseForgeProjectId", "CurseForgeProjectID", "ProjectId", "ProjectID", "CfProjectId",
                            "curseForgeId", "curseforgeId", "curseForgeProjectId", "projectId", "cfProjectId");

                        var score = 0;
                        if (!string.IsNullOrWhiteSpace(name)) score += 3;
                        if (!string.IsNullOrWhiteSpace(version)) score += 2;
                        if (!string.IsNullOrWhiteSpace(author)) score += 1;
                        if (!string.IsNullOrWhiteSpace(slug)) score += 2;
                        if (!string.IsNullOrWhiteSpace(cfId)) score += 5;

                        if (score <= bestScore) continue;
                        bestScore = score;
                        bestManifest = new LocalModManifest
                        {
                            Name = name,
                            Author = author,
                            Version = version,
                            Slug = slug,
                            CurseForgeId = cfId,
                        };
                    }
                }
                catch
                {
                    // ignore entry parse issues
                }
            }

            if (bestManifest == null)
                return false;

            manifest = bestManifest;
            return !string.IsNullOrWhiteSpace(manifest.Name) || !string.IsNullOrWhiteSpace(manifest.CurseForgeId) || !string.IsNullOrWhiteSpace(manifest.Slug);
        }
        catch
        {
            return false;
        }
    }

    private static (string Name, string Version) SplitLocalNameAndVersion(string fileStem)
    {
        if (string.IsNullOrWhiteSpace(fileStem)) return ("", "");

        var stem = fileStem.Trim();

        // Common patterns: "ModName-1.2.3", "ModName_v2", "ModName 2026.1", "ModName-V11"
        var m = Regex.Match(stem, @"(?ix)
            ^(?<name>.*?)(?:[\s_-]+)
            (?<ver>
                v?\d+(?:\.\d+){0,4}(?:[-_\.]?(?:alpha|beta|rc)\d*)?
                |v\d+
                |\d{4}\.\d+(?:\.\d+)?
                |V\d+
            )$");

        if (!m.Success)
            return (stem, "");

        var name = (m.Groups["name"].Value ?? "").Trim().TrimEnd('-', '_');
        var ver = (m.Groups["ver"].Value ?? "").Trim();
        return (string.IsNullOrWhiteSpace(name) ? stem : name, ver);
    }
    
    /// <inheritdoc/>
    public async Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods)
    {
        await _modManifestLock.WaitAsync();
        try
        {
            await SaveInstanceModsUnsafeAsync(instancePath, mods);
        }
        finally
        {
            _modManifestLock.Release();
        }
    }

    private static async Task SaveInstanceModsUnsafeAsync(string instancePath, List<InstalledMod> mods)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        Directory.CreateDirectory(modsPath);
        var manifestPath = Path.Combine(modsPath, "manifest.json");

        var json = JsonSerializer.Serialize(mods, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json);
    }

    /// <inheritdoc/>
    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        if (!HasApiKey())
            return new ModFilesResult();

        try
        {
            var index = page * pageSize;
            var endpoint = $"/v1/mods/{modId}/files?index={index}&pageSize={pageSize}";
            using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Get mod files returned {response.StatusCode}");
                return new ModFilesResult();
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null)
                return new ModFilesResult();
            
            return new ModFilesResult
            {
                Files = cfResponse.Data.Select(f => new ModFileInfo
                {
                    Id = f.Id.ToString(),
                    ModId = f.ModId.ToString(),
                    FileName = f.FileName ?? "",
                    DisplayName = f.DisplayName ?? "",
                    DownloadUrl = f.DownloadUrl ?? "",
                    FileLength = f.FileLength,
                    FileDate = f.FileDate ?? "",
                    ReleaseType = f.ReleaseType,
                    GameVersions = f.GameVersions ?? new List<string>(),
                    DownloadCount = f.DownloadCount
                }).ToList(),
                TotalCount = cfResponse.Pagination?.TotalCount ?? cfResponse.Data.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Get mod files failed: {ex.Message}");
            return new ModFilesResult();
        }
    }

    /// <inheritdoc/>
    public async Task<ModInfo?> GetModAsync(string modIdOrSlug)
    {
        if (!HasApiKey()) return null;
        if (string.IsNullOrWhiteSpace(modIdOrSlug)) return null;

        try
        {
            var resolvedModId = await ResolveCurseForgeModIdAsync(modIdOrSlug);
            if (string.IsNullOrWhiteSpace(resolvedModId)) return null;

            var endpoint = $"/v1/mods/{resolvedModId}";
            using var response = await SendCurseForgeGetWithRetryAsync(endpoint, $"get mod mod={resolvedModId}");
            if (response == null || !response.IsSuccessStatusCode)
            {
                if (response != null)
                    Logger.Warning("ModService", $"Get mod returned {response.StatusCode} for mod={resolvedModId}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var modResp = JsonSerializer.Deserialize<CurseForgeModResponse>(json, _jsonOptions);
            if (modResp?.Data == null) return null;

            return MapToModInfo(modResp.Data);
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Get mod failed: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath)
    {
        if (!HasApiKey())
            return new List<InstalledMod>();
            
        var installedMods = GetInstanceInstalledMods(instancePath);
        var modsWithUpdates = new List<InstalledMod>();

        bool changedAny = false;

        static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var chars = s.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        bool StrongAuthorMatch(InstalledMod mod, CurseForgeMod cf)
        {
            var a = Norm(mod.Author);
            if (string.IsNullOrWhiteSpace(a)) return false;
            var authors = cf.Authors ?? new List<CurseForgeAuthor>();
            return authors.Any(x =>
            {
                var ca = Norm(x.Name);
                if (string.IsNullOrWhiteSpace(ca)) return false;
                return ca == a || ca.Contains(a) || a.Contains(ca);
            });
        }

        async Task<CurseForgeMod?> ResolveProjectAsync(InstalledMod mod)
        {
            if (!string.IsNullOrWhiteSpace(mod.CurseForgeId))
                return null;
            if (string.IsNullOrWhiteSpace(mod.Name))
                return null;

            try
            {
                var filter = Uri.EscapeDataString(mod.Name.Trim());
                var endpoint = $"/v1/mods/search?gameId={HytaleGameId}&searchFilter={filter}&pageSize=25";
                using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, _jsonOptions);
                var candidates = cfResponse?.Data ?? new List<CurseForgeMod>();

                var nameKey = Norm(mod.Name);
                var exact = candidates.Where(c => Norm(c.Name) == nameKey).ToList();
                if (exact.Count == 0) return null;

                var exactAuthor = exact.FirstOrDefault(c => StrongAuthorMatch(mod, c));
                if (exactAuthor != null) return exactAuthor;

                // If author unknown, take the most-downloaded exact name match.
                if (string.IsNullOrWhiteSpace(mod.Author) || string.Equals(mod.Author, "Local file", StringComparison.OrdinalIgnoreCase))
                {
                    return exact.OrderByDescending(c => c.DownloadCount).FirstOrDefault();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        async Task<CurseForgeFile?> InferInstalledFileAsync(InstalledMod mod)
        {
            if (string.IsNullOrWhiteSpace(mod.CurseForgeId)) return null;
            if (string.IsNullOrWhiteSpace(mod.FileName)) return null;

            try
            {
                var endpoint = $"/v1/mods/{mod.CurseForgeId}/files?pageSize=50";
                using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
                var files = cfResponse?.Data ?? new List<CurseForgeFile>();
                if (files.Count == 0) return null;

                var installedFileName = mod.FileName;
                if (installedFileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    installedFileName = Path.GetFileNameWithoutExtension(installedFileName);
                }

                var versionKey = Norm(mod.Version);
                var exactFile = files.FirstOrDefault(f =>
                    !string.IsNullOrWhiteSpace(f.FileName) &&
                    string.Equals(f.FileName, installedFileName, StringComparison.OrdinalIgnoreCase));
                if (exactFile != null) return exactFile;

                CurseForgeFile? best = null;
                var bestScore = -1;
                foreach (var f in files)
                {
                    var score = 0;
                    if (!string.IsNullOrWhiteSpace(versionKey))
                    {
                        if (Norm(f.DisplayName).Contains(versionKey)) score += 3;
                        if (Norm(f.FileName).Contains(versionKey)) score += 2;
                    }

                    // Prefer newer files when otherwise similar
                    if (!string.IsNullOrWhiteSpace(f.FileDate)) score += 1;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = f;
                    }
                }

                return bestScore >= 2 ? best : null;
            }
            catch
            {
                return null;
            }
        }
        
        foreach (var mod in installedMods)
        {
            if (string.IsNullOrEmpty(mod.CurseForgeId))
            {
                var resolved = await ResolveProjectAsync(mod);
                if (resolved != null && resolved.Id > 0)
                {
                    mod.CurseForgeId = resolved.Id.ToString();
                    mod.Slug = resolved.Slug ?? mod.Slug;
                    mod.IconUrl = resolved.Logo?.ThumbnailUrl ?? resolved.Logo?.Url ?? mod.IconUrl;
                    if (!string.IsNullOrWhiteSpace(resolved.Name)) mod.Name = resolved.Name;
                    var resolvedAuthor = resolved.Authors?.FirstOrDefault()?.Name;
                    if (!string.IsNullOrWhiteSpace(resolvedAuthor)) mod.Author = resolvedAuthor;
                    changedAny = true;
                }
            }

            if (string.IsNullOrEmpty(mod.CurseForgeId))
                continue;

            if (string.IsNullOrWhiteSpace(mod.FileId))
            {
                var inferred = await InferInstalledFileAsync(mod);
                if (inferred != null)
                {
                    mod.FileId = inferred.Id.ToString();
                    if (!string.IsNullOrWhiteSpace(inferred.FileName)) mod.FileName = inferred.FileName;
                    mod.FileDate = inferred.FileDate ?? mod.FileDate;
                    mod.ReleaseType = inferred.ReleaseType;
                    changedAny = true;
                }
            }
            
            try
            {
                var endpoint = $"/v1/mods/{mod.CurseForgeId}/files?pageSize=1";
                using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode) continue;
                
                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
                
                var latestFile = cfResponse?.Data?.FirstOrDefault();
                if (latestFile == null) continue;
                
                // If we have a newer file than what's installed
                if (!string.IsNullOrEmpty(mod.FileId) && latestFile.Id.ToString() != mod.FileId)
                {
                    mod.LatestFileId = latestFile.Id.ToString();
                    mod.LatestVersion = latestFile.DisplayName ?? "";
                    modsWithUpdates.Add(mod);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Update check failed for {mod.Name}: {ex.Message}");
            }
        }

        if (changedAny)
        {
            try
            {
                await SaveInstanceModsAsync(instancePath, installedMods);
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Failed to persist resolved mod metadata: {ex.Message}");
            }
        }
        
        return modsWithUpdates;
    }

    /// <inheritdoc/>
    public async Task<bool> InstallLocalModFile(string sourcePath, string instancePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Logger.Warning("ModService", $"Source mod file not found: {sourcePath}");
                return false;
            }
            
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            Directory.CreateDirectory(modsPath);
            
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(modsPath, fileName);
            
            File.Copy(sourcePath, destPath, true);
            
            // Add to manifest
            var mods = GetInstanceInstalledMods(instancePath);
            
            // Remove existing entry with same filename
            mods.RemoveAll(m => m.FileName == fileName);

            var split = SplitLocalNameAndVersion(Path.GetFileNameWithoutExtension(fileName));
            
            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = string.IsNullOrWhiteSpace(split.Name) ? Path.GetFileNameWithoutExtension(fileName) : split.Name,
                FileName = fileName,
                Enabled = true,
                Version = split.Version,
                Author = "Local file"
            });
            
            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModService", $"Installed local mod: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install local mod failed: {ex.Message}");
            return false;
        }
    }
    
    /// <inheritdoc/>
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath)
    {
        try
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            Directory.CreateDirectory(modsPath);
            
            var destPath = Path.Combine(modsPath, fileName);
            var bytes = Convert.FromBase64String(base64Content);
            await File.WriteAllBytesAsync(destPath, bytes);
            
            // Add to manifest
            var mods = GetInstanceInstalledMods(instancePath);
            mods.RemoveAll(m => m.FileName == fileName);

            var split = SplitLocalNameAndVersion(Path.GetFileNameWithoutExtension(fileName));
            
            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = string.IsNullOrWhiteSpace(split.Name) ? Path.GetFileNameWithoutExtension(fileName) : split.Name,
                FileName = fileName,
                Enabled = true,
                Version = split.Version,
                Author = "Imported file"
            });
            
            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModService", $"Installed mod from base64: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install mod from base64 failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// <summary>
    /// Extracts a clean version string from CurseForge DisplayName or FileName.
    /// Looks for semver-like patterns (e.g., "1.2.7", "0.3.1-beta") and returns the first match.
    /// Falls back to DisplayName or FileName if no version pattern is found.
    /// </summary>
    private static string ExtractVersion(string? displayName, string? fileName)
    {
        // Try to extract a semver-like version from displayName first, then fileName
        var versionRegex = new Regex(@"(\d+\.\d+(?:\.\d+)?(?:[-.]\w+)*)");
        
        if (!string.IsNullOrEmpty(displayName))
        {
            var match = versionRegex.Match(displayName);
            if (match.Success) return match.Groups[1].Value;
        }
        
        if (!string.IsNullOrEmpty(fileName))
        {
            // Strip extension first
            var name = fileName;
            if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
                
            var match = versionRegex.Match(name);
            if (match.Success) return match.Groups[1].Value;
        }
        
        return displayName ?? fileName ?? "";
    }

    /// <summary>
    /// Maps a CurseForge API mod to the normalized ModInfo.
    /// </summary>
    private static ModInfo MapToModInfo(CurseForgeMod cfMod)
    {
        return new ModInfo
        {
            Id = cfMod.Id.ToString(),
            Name = cfMod.Name ?? "",
            Slug = cfMod.Slug ?? "",
            Summary = cfMod.Summary ?? "",
            Author = cfMod.Authors?.FirstOrDefault()?.Name ?? "",
            DownloadCount = cfMod.DownloadCount,
            IconUrl = cfMod.Logo?.ThumbnailUrl ?? "",
            ThumbnailUrl = cfMod.Logo?.Url ?? "",
            Categories = cfMod.Categories?.Select(c => c.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            DateUpdated = cfMod.DateModified ?? "",
            LatestFileId = cfMod.LatestFiles?.FirstOrDefault()?.Id.ToString() ?? "",
            Screenshots = cfMod.Screenshots ?? new List<CurseForgeScreenshot>()
        };
    }

    /// <inheritdoc/>
    public async Task<string> GetModFileChangelogAsync(string modId, string fileId)
    {
        if (!HasApiKey()) return string.Empty;
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(fileId)) return string.Empty;

        try
        {
            var endpoint = $"/v1/mods/{modId}/files/{fileId}/changelog";
            using var request = CreateCurseForgeRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Changelog returned {response.StatusCode} for {modId}/{fileId}");
                return string.Empty;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return string.Empty;

            var raw = dataEl.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // CurseForge returns HTML. Convert the common bits to readable plaintext.
            var text = raw;
            text = text.Replace("\r\n", "\n");
            text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*/\s*p\s*>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*/\s*li\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*li\b[^>]*>", "- ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*/\s*(ul|ol)\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*[^>]+>", string.Empty);
            text = WebUtility.HtmlDecode(text);

            // Cleanup excessive blank lines
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Failed to load changelog: {ex.Message}");
            return string.Empty;
        }
    }
}
