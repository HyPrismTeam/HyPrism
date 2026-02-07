namespace HyPrism.Services.Core;

/// <summary>
/// Provides reactive localization with observable translations.
/// </summary>
public interface ILocalizationService
{
    string CurrentLanguage { get; set; }
    string this[string key] { get; }
    
    IObservable<string> GetObservable(string key);
    string Translate(string key, params object[] args);
    void PreloadAllLanguages();
}
