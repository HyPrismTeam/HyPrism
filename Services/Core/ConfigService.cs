using System.Text.Json;
using HyPrism.Models;

namespace HyPrism.Services.Core;

/// <summary>
/// Manages launcher configuration (loading, saving, settings)
/// </summary>
public class ConfigService : IConfigService
{
    private readonly string _configPath;
    private Config _config;
    
    public Config Configuration => _config;
    
    public ConfigService(string appDataPath)
    {
        _configPath = Path.Combine(appDataPath, "config.json");
        _config = LoadConfig();
    }
    
    private Config LoadConfig()
    {
        Config config;
        
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                
                // Apply migrations
                bool needsSave = false;
                
                // Migration: Ensure UUID exists
                if (string.IsNullOrEmpty(config.UUID))
                {
                    config.UUID = Guid.NewGuid().ToString();
                    config.Version = "2.0.0";
                    needsSave = true;
                    Logger.Info("Config", $"Migrated to v2.0.0, UUID: {config.UUID}");
                }
                
                // Migration: Migrate existing UUID to UserUuids mapping
                config.UserUuids ??= new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(config.UUID) && !string.IsNullOrEmpty(config.Nick))
                {
                    var existingKey = config.UserUuids.Keys
                        .FirstOrDefault(k => k.Equals(config.Nick, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingKey == null)
                    {
                        config.UserUuids[config.Nick] = config.UUID;
                        needsSave = true;
                        Logger.Info("Config", $"Migrated existing UUID to UserUuids mapping for '{config.Nick}'");
                    }
                }
                
                // Validate language code exists in available languages
                var availableLanguages = LocalizationService.GetAvailableLanguages();
                if (!string.IsNullOrEmpty(config.Language) && !availableLanguages.ContainsKey(config.Language))
                {
                    // Basic fallback for legacy short codes (e.g. "ru" -> "ru-RU") only if exact match fails
                     var bestMatch = availableLanguages.Keys.FirstOrDefault(k => k.StartsWith(config.Language + "-"));
                     if (bestMatch != null)
                     {
                         config.Language = bestMatch;
                     }
                     else
                     {
                         // Final fallback if totally invalid
                         config.Language = "en-US";
                     }
                     needsSave = true;
                }
                
                // Default nick to random name if empty or placeholder
                
                // Migration: Migrate legacy "latest" branch to release
                if (config.VersionType == "latest")
                {
                    config.VersionType = "release";
                    needsSave = true;
                }
                
                // Default nick to random name if empty or placeholder
                if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player" || config.Nick == "Hyprism" || config.Nick == "HyPrism")
                {
                    config.Nick = UtilityService.GenerateRandomUsername();
                    needsSave = true;
                    Logger.Info("Config", $"Generated random username: {config.Nick}");
                }
                
                if (needsSave)
                {
                    _config = config;
                    SaveConfig();
                }
                
                return config;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to load config: {ex.Message}");
        }
        
        // New config - generate UUID and defaults
        config = new Config();
        if (string.IsNullOrEmpty(config.UUID))
        {
            config.UUID = Guid.NewGuid().ToString();
        }
        
        // Default nick to random name
        if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player")
        {
            config.Nick = UtilityService.GenerateRandomUsername();
        }
        
        // Initialize UserUuids
        config.UserUuids ??= new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(config.Nick) && !string.IsNullOrEmpty(config.UUID))
        {
            config.UserUuids[config.Nick] = config.UUID;
        }
        
        // Validate default language
        var defaultAvailableLanguages = LocalizationService.GetAvailableLanguages();
        if (!defaultAvailableLanguages.ContainsKey(config.Language))
        {
            config.Language = "en-US";
        }
        
        _config = config;
        SaveConfig();
        return config;
    }
    
    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
    
    public void ResetConfig()
    {
        _config = new Config();
        SaveConfig();
    }

    public Task<string?> SetInstanceDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom instance directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _config.InstanceDirectory = null!;
                SaveConfig();
                Logger.Success("Config", "Instance directory cleared, using default");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            // If not rooted, combine with app directory (parent of config file)
            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_configPath)!, expanded));
            }

            Directory.CreateDirectory(expanded);

            _config.InstanceDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Instance directory set to {expanded}");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
}
