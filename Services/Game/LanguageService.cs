using HyPrism.Services.Core;
using System.Diagnostics;

namespace HyPrism.Services.Game;

/// <summary>
/// Manages game language settings and localization file operations.
/// Handles copying language files to game instances and language code mapping.
/// </summary>
public class LanguageService
{
    private readonly VersionService _versionService;
    private readonly InstanceService _instanceService;

    public LanguageService(
        VersionService versionService,
        InstanceService instanceService)
    {
        _versionService = versionService;
        _instanceService = instanceService;
    }

    private string GetLatestInstancePath(string branch)
    {
        var info = _instanceService.LoadLatestInfo(branch);
        if (info != null)
        {
            return _instanceService.ResolveInstancePath(branch, info.Version, true);
        }
        return _instanceService.ResolveInstancePath(branch, 0, true);
    }

    /// <summary>
    /// Sets the game language by copying translated language files to the game's language folder.
    /// Maps launcher locale codes to game locale codes and copies appropriate language files.
    /// </summary>
    /// <param name="languageCode">The launcher language code (e.g., "en", "es", "de", "fr")</param>
    /// <returns>True if language files were successfully copied, false otherwise</returns>
    public async Task<bool> SetGameLanguageAsync(string languageCode)
    {
        try
        {
            // Map launcher language codes to game language folder names
            // Support both short codes (legacy) and standard locale codes
            var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "en-US" }, { "en-US", "en-US" },
                { "es", "es-ES" }, { "es-ES", "es-ES" },
                { "de", "de-DE" }, { "de-DE", "de-DE" },
                { "fr", "fr-FR" }, { "fr-FR", "fr-FR" },
                { "ja", "ja-JP" }, { "ja-JP", "ja-JP" },
                { "ko", "ko-KR" }, { "ko-KR", "ko-KR" },
                { "pt", "pt-BR" }, { "pt-BR", "pt-BR" },
                { "ru", "ru-RU" }, { "ru-RU", "ru-RU" },
                { "tr", "tr-TR" }, { "tr-TR", "tr-TR" },
                { "uk", "uk-UA" }, { "uk-UA", "uk-UA" },
                { "zh", "zh-CN" }, { "zh-CN", "zh-CN" },
                { "be", "be-BY" }, { "be-BY", "be-BY" }
            };

            // Get the game language code
            if (!languageMapping.TryGetValue(languageCode, out var gameLanguageCode))
            {
                Logger.Warning("Language", $"Unknown language code: {languageCode}, defaulting to en-US");
                gameLanguageCode = "en-US";
            }

            Logger.Info("Language", $"Setting game language to: {gameLanguageCode}");

            // Find the game language source directory
            var sourceLangDir = FindLanguageSourceDirectory(gameLanguageCode);

            if (sourceLangDir == null)
            {
                Logger.Warning("Language", $"Language files not found for {gameLanguageCode}");
                return false;
            }

            Logger.Info("Language", $"Found language files at: {sourceLangDir}");

            // Get all installed game versions and update their language files
            var copiedCount = await CopyLanguageFilesToAllInstancesAsync(sourceLangDir, gameLanguageCode);

            Logger.Info("Language", $"Successfully updated language files for {copiedCount} game instance(s)");
            return copiedCount > 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Language", $"Failed to set game language: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the list of available game languages that have translation files.
    /// </summary>
    public List<string> GetAvailableGameLanguages()
    {
        var languages = new List<string>();
        
        var possibleBaseDirs = GetPossibleLanguageBaseDirs();
        
        foreach (var baseDir in possibleBaseDirs)
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var langCode = Path.GetFileName(dir);
                    if (!languages.Contains(langCode))
                    {
                        languages.Add(langCode);
                    }
                }
                break; // Found a valid base directory
            }
        }
        
        return languages;
    }

    /// <summary>
    /// Finds the source directory containing language files for the specified language code.
    /// Checks multiple possible locations (published app, macOS bundle, development).
    /// </summary>
    private static string? FindLanguageSourceDirectory(string gameLanguageCode)
    {
        var possibleLocations = GetPossibleLanguageLocations(gameLanguageCode);
        
        foreach (var loc in possibleLocations)
        {
            if (Directory.Exists(loc))
            {
                return loc;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all possible locations where language files might be stored.
    /// </summary>
    private static List<string> GetPossibleLanguageLocations(string gameLanguageCode)
    {
        var possibleLocations = new List<string>();
        
        // First check if running from published app (game-lang folder next to executable)
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
        
        if (!string.IsNullOrEmpty(exeDir))
        {
            possibleLocations.Add(Path.Combine(exeDir, "game-lang", gameLanguageCode));
            possibleLocations.Add(Path.Combine(exeDir, "..", "Resources", "game-lang", gameLanguageCode)); // macOS bundle
        }
        
        // Development locations
        possibleLocations.Add(Path.Combine(AppContext.BaseDirectory, "game-lang", gameLanguageCode));
        possibleLocations.Add(Path.Combine(AppContext.BaseDirectory, "assets", "game-lang", gameLanguageCode));
        
        return possibleLocations;
    }

    /// <summary>
    /// Gets possible base directories where language folders might be located.
    /// </summary>
    private static List<string> GetPossibleLanguageBaseDirs()
    {
        var possibleBaseDirs = new List<string>();
        
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
        
        if (!string.IsNullOrEmpty(exeDir))
        {
            possibleBaseDirs.Add(Path.Combine(exeDir, "game-lang"));
            possibleBaseDirs.Add(Path.Combine(exeDir, "..", "Resources", "game-lang"));
        }
        
        possibleBaseDirs.Add(Path.Combine(AppContext.BaseDirectory, "game-lang"));
        possibleBaseDirs.Add(Path.Combine(AppContext.BaseDirectory, "assets", "game-lang"));
        
        return possibleBaseDirs;
    }

    /// <summary>
    /// Copies language files to all installed game instances (both release and pre-release).
    /// </summary>
    private async Task<int> CopyLanguageFilesToAllInstancesAsync(string sourceLangDir, string gameLanguageCode)
    {
        int copiedCount = 0;
        var branches = new[] { "release", "pre-release" };

        foreach (var branch in branches)
        {
            try
            {
                var versions = branch == "release" 
                    ? await _versionService.GetVersionListAsync("release") 
                    : await _versionService.GetVersionListAsync("pre-release");
                    
                foreach (var version in versions)
                {
                    var instancePath = _instanceService.ResolveInstancePath(branch, version, true);
                    var targetLangDir = Path.Combine(instancePath, "Client", "Data", "Shared", "language", gameLanguageCode);

                    if (!Directory.Exists(instancePath))
                        continue;

                    // Create target language directory
                    Directory.CreateDirectory(targetLangDir);

                    // Copy all language files
                    await CopyDirectoryRecursiveAsync(sourceLangDir, targetLangDir);
                    copiedCount++;
                    Logger.Info("Language", $"Copied language files to: {targetLangDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Language", $"Failed to update language for branch {branch}: {ex.Message}");
            }
        }

        // Also update "latest" instance if it exists
        foreach (var branch in branches)
        {
            try
            {
                var latestPath = GetLatestInstancePath(branch);
                if (Directory.Exists(latestPath))
                {
                    var targetLangDir = Path.Combine(latestPath, "Client", "Data", "Shared", "language", gameLanguageCode);
                    Directory.CreateDirectory(targetLangDir);
                    await CopyDirectoryRecursiveAsync(sourceLangDir, targetLangDir);
                    copiedCount++;
                    Logger.Info("Language", $"Copied language files to latest: {targetLangDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Language", $"Failed to update latest language for branch {branch}: {ex.Message}");
            }
        }

        return copiedCount;
    }

    /// <summary>
    /// Recursively copies all files from source directory to target directory.
    /// </summary>
    private static async Task CopyDirectoryRecursiveAsync(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var targetPath = Path.Combine(targetDir, fileName);
            await Task.Run(() => File.Copy(file, targetPath, overwrite: true));
        }

        // Copy subdirectories
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var targetSubDir = Path.Combine(targetDir, dirName);
            await CopyDirectoryRecursiveAsync(dir, targetSubDir);
        }
    }
}
