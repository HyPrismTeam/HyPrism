using Avalonia;
using Avalonia.Media;
using ReactiveUI;

namespace HyPrism.Services.Core;

public class ThemeService : ReactiveObject
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    public ThemeService()
    {
        // Depends on existing Backend architecture
        // We can access ConfigService via AppService or creating a new instance if needed, 
        // but ideally we should hook into the main AppService singleton.
        // For now, we will assume we can get the config from the config service directly 
        // OR simply observe the current config.
        
        // However, better approach in this codebase seems to be accessing the single AppService instance
        // But AppService is in Backend. 
        // Let's just create a new ConfigService since it points to the same file 
        // OR better: rely on the value being passed or set.
    }

    /// <summary>
    /// Applies the accent color from the configuration to the Application Resources.
    /// </summary>
    public void ApplyAccentColor(string hexColor)
    {
        if (Color.TryParse(hexColor, out Color color))
        {
            if (Application.Current != null)
            {
                Application.Current.Resources["SystemAccentColor"] = color;
                Application.Current.Resources["SystemAccentBrush"] = new SolidColorBrush(color);
            }
        }
    }

    /// <summary>
    /// Initialize with current config
    /// </summary>
    public void Initialize(string initialColor)
    {
        ApplyAccentColor(initialColor);
    }
}
