using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AsyncImageLoader;
using HyPrism.UI.MainWindow;
using HyPrism.Services.Core;
using HyPrism.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Avalonia.Threading;

namespace HyPrism.UI;

public partial class App : Application
{
    // DI Container
    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Initialize AsyncImageLoader with disk cache + thumbnail decoding.
        // Images are saved to disk at full quality but decoded at 240px width max,
        // which reduces native SkiaSharp memory from ~8 MB to ~130 KB per image.
        var cacheDir = Path.Combine(UtilityService.GetEffectiveAppDir(), "Cache", "Images");
        Directory.CreateDirectory(cacheDir);
        ImageLoader.AsyncImageLoader = new DiskOnlyCachedWebImageLoader(cacheDir, decodeWidth: 240);
        
        // Initialize DI
        Services = Bootstrapper.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize simple services like theme
        // Use ConfigService from DI instead of creating a new instance
        try 
        {
            var configService = Services!.GetRequiredService<ConfigService>();
            var themeService = Services!.GetRequiredService<IThemeService>();
            themeService.Initialize(configService.Configuration.AccentColor);
            
            // Set static accessor for XAML markup extensions
            LocalizationService.Current = Services!.GetRequiredService<LocalizationService>();
        }
        catch { /* ignore, fallback to default */ }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve MainViewModel from the container
            var mainVm = Services!.GetRequiredService<MainViewModel>();
            
            // Subscribe to accent color changes
            var settingsService = Services!.GetRequiredService<SettingsService>();
            var themeService = Services!.GetRequiredService<IThemeService>();
            settingsService.OnAccentColorChanged += (color) =>
            {
                Dispatcher.UIThread.InvokeAsync(() => themeService.ApplyAccentColor(color));
            };
            
            desktop.MainWindow = new HyPrism.UI.MainWindow.MainWindow
            {
                DataContext = mainVm
            };
            
            // Ensure proper cleanup on shutdown to release all managed/native resources
            desktop.ShutdownRequested += (_, _) =>
            {
                (mainVm.DashboardViewModel as IDisposable)?.Dispose();
                (Services as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
