using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Reactive.Subjects;
using System.Reflection;

namespace HyPrism.Backend.Services.Core;

public class LocalizationService
{
    private Dictionary<string, string> _translations = new();
    private JsonElement? _rawJson;
    private string _currentLanguage = "en";
    
    private readonly BehaviorSubject<string> _languageChanged = new("");
    public IObservable<string> LanguageChanged => _languageChanged;
    
    public static readonly Dictionary<string, string> AvailableLanguages = new()
    {
        { "en", "English" },
        { "ru", "Русский" },
        { "de", "Deutsch" },
        { "es", "Español" },
        { "fr", "Français" },
        { "ja", "日本語" },
        { "ko", "한국어" },
        { "pt", "Português" },
        { "tr", "Türkçe" },
        { "uk", "Українська" },
        { "zh", "中文" },
        { "be", "Беларуская" }
    };
    
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value && AvailableLanguages.ContainsKey(value))
            {
                _currentLanguage = value;
                LoadLanguage(value);
                _languageChanged.OnNext(value);
            }
        }
    }
    
    public LocalizationService()
    {
        LoadLanguage("en"); // Default to English
    }
    
    private void LoadLanguage(string languageCode)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"HyPrism.Assets.Locales.{languageCode}.json";
        
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                Logger.Warning("Localization", $"Language file not found: {resourceName}, using English");
                
                // Try English as fallback
                if (languageCode != "en")
                {
                    LoadLanguage("en");
                    return;
                }
                
                _translations = new Dictionary<string, string>();
                return;
            }
            
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            
            // Parse as JsonDocument to support nested keys
            using var doc = JsonDocument.Parse(json);
            _rawJson = doc.RootElement.Clone();
            
            // Flatten for compatibility with old-style key lookups
            _translations = new Dictionary<string, string>();
            FlattenJson(_rawJson.Value, "", _translations);
            
            Logger.Info("Localization", $"Loaded {_translations.Count} translations for '{languageCode}'");
        }
        catch (Exception ex)
        {
            Logger.Error("Localization", $"Failed to load language file: {ex.Message}");
            _translations = new Dictionary<string, string>();
            _rawJson = null;
        }
    }
    
    private void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                FlattenJson(property.Value, key, result);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            result[prefix] = element.GetString() ?? "";
        }
    }
    
    public string Translate(string key, params object[] args)
    {
        if (_translations.TryGetValue(key, out var translation))
        {
            // Simple placeholder replacement {0}, {1}, etc.
            if (args.Length > 0)
            {
                try
                {
                    return string.Format(translation, args);
                }
                catch
                {
                    return translation;
                }
            }
            return translation;
        }
        
        // Return key if translation not found
        return key;
    }
    
    public string this[string key] => Translate(key);
}
