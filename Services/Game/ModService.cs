using HyPrism.Services.Core;
using System.Text.Json;
using HyPrism.Models;
using System.Text.RegularExpressions;

namespace HyPrism.Services.Game;

/// <summary>
/// Manages game modifications including searching, installing, updating, and tracking.
/// Currently contains stubs pending a complete rewrite for proper mod support.
/// </summary>
/// <remarks>
/// The mod system is designed to integrate with CurseForge and handle mod dependencies.
/// Current implementation is disabled pending backend changes.
/// </remarks>
public class ModService : IModService
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    // CurseForge API base URL
    private const string CfApiBaseUrl = "https://api.curseforge.com";
    
    // CF Website Base URL
    private const string CfBaseUrl = "https://www.curseforge.com";

    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);

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
    /// <param name="httpClient">The HTTP client for API requests.</param>
    /// <param name="appDir">The application data directory path.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="instanceService">The instance management service.</param>
    /// <param name="progressNotificationService">The progress notification service.</param>
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
    /// <param name="method">The HTTP method.</param>
    /// <param name="endpoint">The API endpoint (without base URL).</param>
    /// <returns>Configured HttpRequestMessage with API key header.</returns>
    private HttpRequestMessage CreateCurseForgeRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, $"{CfApiBaseUrl}{endpoint}");
        request.Headers.Add("x-api-key", CurseForgeApiKey);
        request.Headers.Add("Accept", "application/json");
        return request;
    }
    
    /// <inheritdoc/>
    /// <remarks>Currently disabled pending mod system rewrite.</remarks>
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        Logger.Warning("ModService", "Search disabled pending rewrite.");
        return await Task.FromResult(new ModSearchResult
        {
            Mods = new List<ModInfo>(),
            TotalCount = 0
        });
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    /// <remarks>Currently disabled pending mod system rewrite.</remarks>
    public async Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null)
    {
        Logger.Warning("ModService", "Installation disabled pending rewrite.");
        return await Task.FromResult(false);
    }

    /// <inheritdoc/>
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
    
    /// <inheritdoc/>
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
