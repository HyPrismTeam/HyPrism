using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
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
        
        // Initialize AsyncImageLoader with Disk Cache to save RAM
        var cacheDir = Path.Combine(UtilityService.GetEffectiveAppDir(), "Cache", "Images");
        Directory.CreateDirectory(cacheDir);
        ImageLoader.AsyncImageLoader = new DiskCachedWebImageLoader(cacheDir);
        
        // Initialize DI
        Services = Bootstrapper.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize simple services like theme
        // We need to peek at the config to set the initial color
        // Since MainViewModel will initialize AppService, we can grab it from there or do a quick separate read.
        // For simplicity, we'll let MainViewModel handle the logic or just read it once here.
        
        // Quick read to set initial color before window shows
        try 
        {
            var configService = new ConfigService(UtilityService.GetEffectiveAppDir());
            ThemeService.Instance.Initialize(configService.Configuration.AccentColor);
        }
        catch { /* ignore, fallback to default */ }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve MainViewModel from the container
            var mainVm = Services!.GetRequiredService<MainViewModel>();
            
            // Subscribe to accent color changes
            var settingsService = Services!.GetRequiredService<SettingsService>();
            settingsService.OnAccentColorChanged += (color) =>
            {
                Dispatcher.UIThread.InvokeAsync(() => ThemeService.Instance.ApplyAccentColor(color));
            };
            
            desktop.MainWindow = new HyPrism.UI.MainWindow.MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
