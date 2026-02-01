using System;
using System.IO;
using System.Text.Json;
using HyPrism.Backend.Models;

namespace HyPrism.Backend.Services.Core;

/// <summary>
/// Manages launcher configuration (loading, saving, settings)
/// </summary>
public class ConfigService
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
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                return config;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load config: {ex.Message}");
        }
        
        return new Config();
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
}
