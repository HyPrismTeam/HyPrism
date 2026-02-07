using HyPrism.Models;

namespace HyPrism.Services.Game;

public interface IModService
{
    Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder);
    Task<List<ModCategory>> GetModCategoriesAsync();
    Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null);
    List<InstalledMod> GetInstanceInstalledMods(string instancePath);
    Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods);
    Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize);
    Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath);
    Task<bool> InstallLocalModFile(string sourcePath, string instancePath);
    Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath);
}
