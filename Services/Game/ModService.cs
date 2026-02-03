using HyPrism.Services.Core;
using System.Text.Json;
using HyPrism.Models;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for managing mods - searching, installing, updating, and managing mod lists.
/// </summary>
public class ModService
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int HytaleGameId = 79453;
    private const string CurseForgeApiKey = "$2a$10$bL4bIL5pUWqfcO7KQtnMReakwtfHbNKh6v1uTpKlzhwoueEJQnPnm";
    
    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);

    private readonly ConfigService _configService;
    private readonly InstanceService _instanceService;
    private readonly ProgressNotificationService _progressNotificationService;

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
    /// Search for mods on CurseForge.
    /// </summary>
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
            
            using var response = await _httpClient.SendAsync(request);
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
                IconUrl = m.Logo?.Url ?? "",
                ThumbnailUrl = m.Logo?.ThumbnailUrl ?? "",
                Categories = m.Categories?.Select(c => c.Name ?? "").ToList() ?? new List<string>(),
                DateUpdated = m.DateModified ?? "",
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
            Logger.Error("ModService", $"Failed to search mods: {ex.Message}");
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
        }
    }

    /// <summary>
    /// Get available files for a specific mod.
    /// </summary>
    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/mods/{modId}/files";
            
            if (pageSize > 0)
            {
                url += $"?pageSize={pageSize}";
            }
            
            if (page > 0)
            {
                url += $"&index={page * pageSize}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await _httpClient.SendAsync(request);
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
                FileName = f.FileName ?? "",
                DisplayName = f.DisplayName ?? f.FileName ?? "",
                DownloadUrl = f.DownloadUrl ?? "",
                FileLength = f.FileLength,
                FileDate = f.FileDate ?? "",
                ReleaseType = f.ReleaseType,
                GameVersions = new List<string>()
            }).ToList();
            
            return new ModFilesResult
            {
                Files = files,
                TotalCount = cfResponse.Pagination?.TotalCount ?? files.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to get mod files: {ex.Message}");
            return new ModFilesResult { Files = new List<ModFileInfo>(), TotalCount = 0 };
        }
    }

    /// <summary>
    /// Get list of mod categories from CurseForge.
    /// </summary>
    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/categories?gameId={HytaleGameId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await _httpClient.SendAsync(request);
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
            
            return cfResponse.Data
                .Where(c => c.IsClass == false)
                .Select(c => new ModCategory
                {
                    Id = c.Id,
                    Name = c.Name ?? "",
                    Slug = c.Slug ?? ""
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to get mod categories: {ex.Message}");
            return new List<ModCategory>();
        }
    }

    /// <summary>
    /// Get installed mods for a specific instance.
    /// </summary>
    public List<InstalledMod> GetInstanceInstalledMods(string instancePath)
    {
        var manifestPath = Path.Combine(instancePath, "mod_manifest.json");
        
        if (!File.Exists(manifestPath))
        {
            return new List<InstalledMod>();
        }
        
        try
        {
            var json = File.ReadAllText(manifestPath);
            var mods = JsonSerializer.Deserialize<List<InstalledMod>>(json);
            return mods ?? new List<InstalledMod>();
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to load mod manifest: {ex.Message}");
            return new List<InstalledMod>();
        }
    }

    /// <summary>
    /// Save installed mods manifest for an instance.
    /// </summary>
    public async Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods)
    {
        await _modManifestLock.WaitAsync();
        try
        {
            var manifestPath = Path.Combine(instancePath, "mod_manifest.json");
            var json = JsonSerializer.Serialize(mods, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, json);
        }
        finally
        {
            _modManifestLock.Release();
        }
    }

    /// <summary>
    /// Install a mod from base64 content.
    /// </summary>
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath)
    {
        try
        {
            var modsPath = Path.Combine(instancePath, "Client", "mods");
            Directory.CreateDirectory(modsPath);
            
            var modFilePath = Path.Combine(modsPath, fileName);
            var bytes = Convert.FromBase64String(base64Content);
            await File.WriteAllBytesAsync(modFilePath, bytes);
            
            Logger.Success("ModService", $"Installed mod: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to install mod: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Install a mod from a local file.
    /// </summary>
    public async Task<bool> InstallLocalModFile(string sourcePath, string instancePath)
    {
        try
        {
            var modsPath = Path.Combine(instancePath, "Client", "mods");
            Directory.CreateDirectory(modsPath);
            
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(modsPath, fileName);
            
            File.Copy(sourcePath, destPath, overwrite: true);
            
            Logger.Success("ModService", $"Installed local mod: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to install local mod: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Install a mod file from CurseForge to an instance.
    /// </summary>
    public async Task<bool> InstallModFileToInstanceAsync(string modId, string fileId, string instancePath, Action<string, string>? onProgress = null)
    {
        try
        {
            string modsPath = Path.Combine(instancePath, "Client", "mods");
            Directory.CreateDirectory(modsPath);
            
            Logger.Info("ModService", $"Installing mod to: {modsPath}");
            
            // Get file info from CurseForge
            var url = $"{CurseForgeBaseUrl}/mods/{modId}/files/{fileId}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFileResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (cfResponse?.Data == null || string.IsNullOrEmpty(cfResponse.Data.DownloadUrl))
            {
                Logger.Error("ModService", "Could not get download URL for mod file");
                return false;
            }
            
            var fileInfo = cfResponse.Data;
            var fileName = fileInfo.FileName ?? $"mod_{modId}_{fileId}.jar";
            var filePath = Path.Combine(modsPath, fileName);

            // Fetch mod metadata
            var modMeta = await GetCurseForgeModAsync(modId);
            string modName = modMeta?.Name ?? fileInfo.DisplayName ?? fileName;
            string modAuthor = modMeta?.Authors?.FirstOrDefault()?.Name ?? "Unknown";
            string modDescription = modMeta?.Summary ?? "";
            string iconUrl = modMeta?.Logo?.ThumbnailUrl ?? modMeta?.Logo?.Url ?? "";
            string modSlug = modMeta?.Slug ?? "";
            var screenshots = modMeta?.Screenshots ?? new List<CurseForgeScreenshot>();
            
            // Download the file
            onProgress?.Invoke("downloading", $"Downloading {fileName}...");
            Logger.Info("ModService", $"Downloading {fileName}...");
            
            using var downloadResponse = await _httpClient.GetAsync(fileInfo.DownloadUrl);
            downloadResponse.EnsureSuccessStatusCode();
            
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await downloadResponse.Content.CopyToAsync(fs);
            
            Logger.Success("ModService", $"Installed {fileName}");
            onProgress?.Invoke("complete", $"Installed {fileName}");
            
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
                        installedMods = JsonSerializer.Deserialize<List<InstalledMod>>(manifestJson) ?? new List<InstalledMod>();
                    }
                    catch { }
                }
                
                // Remove old version if exists
                var existingMod = installedMods.FirstOrDefault(m => m.Id == $"cf-{modId}" || m.CurseForgeId == modId);
                if (existingMod != null && !string.IsNullOrEmpty(existingMod.FileName) && existingMod.FileName != fileName)
                {
                    var oldFilePath = Path.Combine(modsPath, existingMod.FileName);
                    if (File.Exists(oldFilePath))
                    {
                        try
                        {
                            File.Delete(oldFilePath);
                            Logger.Info("ModService", $"Deleted old version: {existingMod.FileName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("ModService", $"Failed to delete old mod file: {ex.Message}");
                        }
                    }
                }
                
                // Remove existing entry
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
                    Author = modAuthor,
                    Description = modDescription,
                    IconUrl = iconUrl,
                    FileDate = fileInfo.FileDate ?? DateTime.UtcNow.ToString("o"),
                    Screenshots = screenshots,
                    LatestFileId = fileInfo.Id.ToString(),
                    LatestVersion = fileInfo.DisplayName ?? fileName
                });
                
                var updatedManifestJson = JsonSerializer.Serialize(installedMods, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(manifestPath, updatedManifestJson);
            }
            finally
            {
                _modManifestLock.Release();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to install mod file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get CurseForge mod metadata.
    /// </summary>
    private async Task<CurseForgeMod?> GetCurseForgeModAsync(string modId)
    {
        try
        {
            var url = $"{CurseForgeBaseUrl}/mods/{modId}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", CurseForgeApiKey);
            
            using var response = await _httpClient.SendAsync(request);
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
            Logger.Warning("ModService", $"Failed to get mod metadata: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check for updates for installed mods.
    /// </summary>
    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath)
    {
        var modsNeedingUpdate = new List<InstalledMod>();
        var installedMods = GetInstanceInstalledMods(instancePath);
        
        foreach (var mod in installedMods.Where(m => !string.IsNullOrEmpty(m.CurseForgeId)))
        {
            try
            {
                var modMeta = await GetCurseForgeModAsync(mod.CurseForgeId);
                if (modMeta == null) continue;
                
                var latestFile = modMeta.LatestFiles?.FirstOrDefault();
                if (latestFile == null) continue;
                
                var latestFileId = latestFile.Id.ToString();
                
                if (latestFileId != mod.FileId && latestFileId != mod.LatestFileId)
                {
                    mod.LatestFileId = latestFileId;
                    mod.LatestVersion = latestFile.DisplayName ?? latestFile.FileName ?? "";
                    modsNeedingUpdate.Add(mod);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Failed to check update for {mod.Name}: {ex.Message}");
            }
        }
        
        return modsNeedingUpdate;
    }

    /// <summary>
    /// Import a mod list from JSON file.
    /// </summary>
    public async Task<int> ImportModListAsync(string modListPath, string instancePath, Action<int, int>? onProgress = null)
    {
        try
        {
            var json = await File.ReadAllTextAsync(modListPath);
            var modList = JsonSerializer.Deserialize<List<ModListEntry>>(json);
            
            if (modList == null || modList.Count == 0)
            {
                Logger.Warning("ModService", "Mod list is empty or invalid");
                return 0;
            }
            
            int successCount = 0;
            int totalMods = modList.Count;
            
            for (int i = 0; i < totalMods; i++)
            {
                var modEntry = modList[i];
                onProgress?.Invoke(i + 1, totalMods);
                
                if (string.IsNullOrEmpty(modEntry.CurseForgeId) || string.IsNullOrEmpty(modEntry.FileId))
                {
                    Logger.Warning("ModService", $"Skipping invalid mod entry: {modEntry.Name}");
                    continue;
                }
                
                var success = await InstallModFileToInstanceAsync(
                    modEntry.CurseForgeId, 
                    modEntry.FileId, 
                    instancePath,
                    (status, msg) => Logger.Info("ModService", msg)
                );
                
                if (success) successCount++;
            }
            
            Logger.Success("ModService", $"Imported {successCount}/{totalMods} mods");
            return successCount;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Failed to import mod list: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Installs a predefined list of optimization mods to the current game instance.
    /// Optimization mods improve game performance and FPS.
    /// </summary>
    public async Task<bool> InstallOptimizationModsAsync()
    {
        try
        {
            // Predefined list of optimization mod IDs from CurseForge
            var optimizationModIds = new List<string>
            {
                // Example: "sodium", "lithium", "phosphor", "ferritecore", etc.
            };

            if (optimizationModIds.Count == 0)
            {
                Logger.Warning("OptimizationMods", "No optimization mods defined yet - feature is a stub");
                return false;
            }

            // Get current game instance path
            var branch = UtilityService.NormalizeVersionType(_configService.Configuration.VersionType);
            var instancePath = _instanceService.GetLatestInstancePath(branch);
            
            if (!_instanceService.IsClientPresent(instancePath))
            {
                Logger.Warning("OptimizationMods", "Game client not installed - cannot install optimization mods");
                return false;
            }

            // Install each optimization mod
            foreach (var modId in optimizationModIds)
            {
                try
                {
                    // Get latest mod file
                    var filesResult = await GetModFilesAsync(modId, 1, 10);
                    if (filesResult.Files.Count == 0) continue;

                    var latestFile = filesResult.Files[0];
                    
                    // Install mod to instance
                    await InstallModFileToInstanceAsync(
                        modId, 
                        latestFile.Id.ToString(), 
                        instancePath,
                        (stage, msg) => _progressNotificationService.SendProgress(
                            "optimization-mods", 
                            0, 
                            $"Installing {latestFile.DisplayName}: {msg}", 
                            0, 
                            100
                        )
                    );
                    
                    Logger.Success("OptimizationMods", $"Installed optimization mod: {latestFile.DisplayName}");
                }
                catch (Exception ex)
                {
                    Logger.Warning("OptimizationMods", $"Failed to install mod {modId}: {ex.Message}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("OptimizationMods", $"InstallOptimizationModsAsync error: {ex.Message}");
            _progressNotificationService.SendErrorEvent("Optimization Mods Installation Error", ex.Message, null);
            return false;
        }
    }
}
