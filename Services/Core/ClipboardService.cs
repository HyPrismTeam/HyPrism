using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace HyPrism.Services.Core;

/// <summary>
/// Clipboard service implementation using Avalonia's clipboard API.
/// This isolates Avalonia UI dependency from ViewModels.
/// </summary>
public class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            return await clipboard.GetTextAsync();
        }
        return null;
    }

    private static Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }
}
