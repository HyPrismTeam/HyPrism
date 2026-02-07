namespace HyPrism.Services.Core;

/// <summary>
/// Abstracts clipboard operations away from ViewModels to maintain MVVM pattern.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}
