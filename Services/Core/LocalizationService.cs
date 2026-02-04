using System.Reactive.Linq;
using System.Text.Json;
using System.Reflection;
using ReactiveUI;

namespace HyPrism.Services.Core;

public class LocalizationService : ReactiveObject
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();
    
    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "en-US";
    
    // Cache for available languages to avoid scanning assembly every time
    private static Dictionary<string, string>? _cachedAvailableLanguages;

    // Cache for loaded translations: Key=LanguageCode, Value=Dictionary of translations
    private Dictionary<string, Dictionary<string, string>> _languageCache = new();

    /// <summary>
    /// Gets available languages by scanning embedded resources.
    /// Returns Dictionary where Key = language code (e.g. "ru-RU"), Value = native name (e.g. "Русский")
    /// </summary>
    public static Dictionary<string, string> GetAvailableLanguages()
    {
        if (_cachedAvailableLanguages != null)
            return _cachedAvailableLanguages;

        var result = new Dictionary<string, string>();
        var assembly = Assembly.GetExecutingAssembly();
        // Standard namespace + folder structure "HyPrism.Assets.Locales." 
        // But let's filter generically to be safe
        var suffix = ".json";

        var resourceNames = assembly.GetManifestResourceNames();
        
        foreach (var resourceName in resourceNames)
        {
            if (resourceName.Contains(".Locales.") && resourceName.EndsWith(suffix))
            {
                // Extract code: HyPrism.Assets.Locales.en-US.json -> en-US
                // We assume the segment between Locales. and .json is the code
                // But we must handle cases where prefix is different if namespace changed
                
                var parts = resourceName.Split('.');
                // Expected: [..., "Locales", "en-US", "json"]
                // Code is parts[parts.Length - 2]
                
                if (parts.Length >= 2)
                {
                    var langCode = parts[parts.Length - 2];
                    
                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream != null)
                        {
                            using var reader = new StreamReader(stream);
                            var json = reader.ReadToEnd();
                            using var doc = JsonDocument.Parse(json);
                            
                            // Read _langName
                            if (doc.RootElement.TryGetProperty("_langName", out var langNameElement))
                            {
                                var langName = langNameElement.GetString();
                                result[langCode] = !string.IsNullOrEmpty(langName) ? langName : langCode;
                            }
                            else
                            {
                                result[langCode] = langCode;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("Localization", $"Failed to parse locale {langCode}: {ex.Message}");
                    }
                }
            }
        }
        
        // Ensure en-US is always present as fallback
        if (!result.ContainsKey("en-US"))
        {
            result["en-US"] = "English";
        }

        _cachedAvailableLanguages = result;
        return result;
    }
    
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
             // Force refresh if needed, or rely on cache. 
             // Ideally we check keys of cached dict.
             var available = GetAvailableLanguages();
             if (!available.ContainsKey(value))
             {
                 Logger.Warning("Localization", $"Invalid language code: {value}, keeping: {_currentLanguage}");
                 return;
             }
             
            // Only load if actually changed
            if (_currentLanguage != value)
            {
                // LOAD FIRST: Update the translation dictionary before notifying UI
                LoadLanguage(value);
                
                // NOTIFY SECOND: Now that the dictionary is ready, tell UI to refresh
                this.RaiseAndSetIfChanged(ref _currentLanguage, value);
            }
        }
    }
    
    public LocalizationService()
    {
        // Preload cache
        Task.Run(PreloadAllLanguages); 
        LoadLanguage("en-US"); // Default to English immediately
    }
    
    /// <summary>
    /// Loads all available languages into memory cache
    /// </summary>
    public void PreloadAllLanguages()
    {
        var languages = GetAvailableLanguages();
        foreach (var lang in languages.Keys)
        {
            if (!_languageCache.ContainsKey(lang))
            {
                LoadLanguageInternal(lang);
            }
        }
        Logger.Success("Localization", "All translations successfully loaded");
    }

    /// <summary>
    /// Creates an observable that tracks a specific translation key.
    /// This is the key method for reactive bindings!
    /// </summary>
    public IObservable<string> GetObservable(string key)
    {
        return this.WhenAnyValue(x => x.CurrentLanguage)
            .Select(_ => Translate(key));
    }
    
    private void LoadLanguage(string languageCode)
    {
        // Check cache first
        if (_languageCache.TryGetValue(languageCode, out var cachedTranslations))
        {
            _translations = cachedTranslations;
            Logger.Info("Localization", $"Loaded language '{languageCode}' from memory cache");
            return;
        }

        var loaded = LoadLanguageInternal(languageCode);
        if (loaded != null)
        {
            _translations = loaded;
        }
        else
        {
             // Fallback to English if load failed
            if (languageCode != "en-US")
            {
                Logger.Warning("Localization", $"Falling back to English from {languageCode}");
                LoadLanguage("en-US");
            }
        }
    }

    private Dictionary<string, string>? LoadLanguageInternal(string languageCode)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"HyPrism.Assets.Locales.{languageCode}.json";
        
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                Logger.Warning("Localization", $"Language file not found: {resourceName}");
                return null;
            }
            
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            
            // Parse as JsonDocument to support nested keys
            using var doc = JsonDocument.Parse(json);
            
            // Flatten for compatibility with old-style key lookups
            var translations = new Dictionary<string, string>();
            FlattenJson(doc.RootElement, "", translations);
            
            // Add to cache
            lock (_languageCache) 
            {
                _languageCache[languageCode] = translations;
            }
            
            return translations;
        }
        catch (Exception ex)
        {
            Logger.Error("Localization", $"Failed to load language file '{languageCode}': {ex.Message}");
            return null;
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
        string? translation = null;
        
        // Try current language
        if (_translations.TryGetValue(key, out var val))
        {
            translation = val;
        }
        // Fallback to English if missing
        else if (_currentLanguage != "en-US")
        {
            // Safe access to cache? We should probably lock or rely on the fact that en-US is loaded early
            // Locking is safer.
            lock (_languageCache)
            {
                if (_languageCache.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var enVal))
                {
                    translation = enVal;
                }
            }
        }

        if (translation != null)
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
    
    // Indexer for easier access, but note: ReactiveObject doesn't auto-notify for indexers
    // Instead, we notify via property name in CurrentLanguage setter
    public string this[string key] => Translate(key);
}
