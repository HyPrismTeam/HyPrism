using HyPrism.Services.Core;
using System.Text.Json;
using HyPrism.Models;
using System.Text.RegularExpressions;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for managing mods - searching, installing, updating, and managing mod lists.
/// Stubs for future rewrite.
/// </summary>
public class ModService : IModService
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    // CF Base URL
    private const string CfBaseUrl = "https://www.curseforge.com";

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
    /// Search stub.
    /// </summary>
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        Logger.Warning("ModService", "Search disabled pending rewrite.");
        return await Task.FromResult(new ModSearchResult
        {
            Mods = new List<ModInfo>(),
            TotalCount = 0
        });
    }

    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        return await Task.FromResult(new List<ModCategory>
        {
            new ModCategory { Id = 1, Name = "All Mods", Slug = "all" },
            new ModCategory { Id = 2, Name = "World Gen", Slug = "world-gen" },
            new ModCategory { Id = 3, Name = "Magic", Slug = "magic" },
            new ModCategory { Id = 4, Name = "Tech", Slug = "tech" }
        });
    }

    /// <summary>
    /// Install stub.
    /// </summary>
    public async Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null)
    {
        Logger.Warning("ModService", "Installation disabled pending rewrite.");
        return await Task.FromResult(false);
    }

    public List<InstalledMod> GetInstanceInstalledMods(string instancePath)
    {
        var modsPath = Path.Combine(instancePath, "Client", "mods");
        var manifestPath = Path.Combine(modsPath, "manifest.json");

        if (!File.Exists(manifestPath)) return new List<InstalledMod>(); 
        
        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<List<InstalledMod>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InstalledMod>();
        }
        catch 
        {
            return new List<InstalledMod>();
        }
    }
    
    public async Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods)
    {
        await _modManifestLock.WaitAsync();
        try
        {
            var modsPath = Path.Combine(instancePath, "Client", "mods");
            Directory.CreateDirectory(modsPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            var json = JsonSerializer.Serialize(mods, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, json);
        }
        finally
        {
            _modManifestLock.Release();
        }
    }

    // Stub
    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        return await Task.FromResult(new ModFilesResult());
    }

    // Stub
    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath)
    {
        return await Task.FromResult(new List<InstalledMod>());
    }

    // Stub
    public async Task<bool> InstallLocalModFile(string sourcePath, string instancePath)
    {
        return await Task.FromResult(false);
    }
    
    // Stub
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath)
    {
        return await Task.FromResult(false);
    }
}
