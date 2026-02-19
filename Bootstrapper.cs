using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using HyPrism.Services;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Platform;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Ipc;
using HyPrism.Services.User;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Asset;
using HyPrism.Services.Game.Auth;
using HyPrism.Services.Game.Butler;
using HyPrism.Services.Game.Download;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.Game.Mod;
using HyPrism.Services.Game.Sources;
using HyPrism.Services.Game.Version;

namespace HyPrism;

public static class Bootstrapper
{
    /// <summary>
    /// URL parts for fetching CurseForge API key
    /// Per legacy policy, the key cannot be stored in plain text
    /// </summary>
    private static string CurseForgeKeySourceUrl => string.Concat(
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tLw==")),
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("UHJpc21MYXVuY2hlci9QcmlzbUxhdW5jaGVy")),
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("L2RldmVsb3AvQ01ha2VMaXN0cy50eHQ=")));
    
    public static IServiceProvider Initialize()
    {
        Logger.Info("Bootstrapper", "Initializing application services...");
        try
        {
            var services = new ServiceCollection();

            #region Core Infrastructure & Configuration

            var appDir = UtilityService.GetEffectiveAppDir();
            services.AddSingleton(new AppPathConfiguration(appDir));

            services.AddSingleton(_ => 
            {
                var client = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(30)
                };
                client.DefaultRequestHeaders.Add("User-Agent", "HyPrism/1.0");
                return client;
            });

            // Config
            services.AddSingleton<ConfigService>(sp =>
                new ConfigService(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IConfigService>(sp => sp.GetRequiredService<ConfigService>());

            #endregion

            #region Data & Utility Services

            services.AddSingleton<NewsService>();
            services.AddSingleton<INewsService>(sp => sp.GetRequiredService<NewsService>());

            services.AddSingleton(sp =>
                new ProfileService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<AvatarService>()));
            services.AddSingleton<IProfileService>(sp => sp.GetRequiredService<ProfileService>());

            services.AddSingleton<DownloadService>();
            services.AddSingleton<IDownloadService>(sp => sp.GetRequiredService<DownloadService>());

            services.AddSingleton(sp =>
                new GitHubService(sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IGitHubService>(sp => sp.GetRequiredService<GitHubService>());

            services.AddSingleton(sp =>
                new VersionService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<HytaleVersionSource>(),
                    MirrorLoaderService.LoadAll(
                        sp.GetRequiredService<AppPathConfiguration>().AppDir,
                        sp.GetRequiredService<HttpClient>())));
            services.AddSingleton<IVersionService>(sp => sp.GetRequiredService<VersionService>());

            #endregion

            #region Game & Instance Management

            services.AddSingleton(sp =>
                new InstanceService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>()));
            services.AddSingleton<IInstanceService>(sp => sp.GetRequiredService<InstanceService>());

            services.AddSingleton(sp =>
                new ModService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
            services.AddSingleton<IModService>(sp => sp.GetRequiredService<ModService>());

            services.AddSingleton(sp =>
                new LaunchService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<ILaunchService>(sp => sp.GetRequiredService<LaunchService>());

            services.AddSingleton(sp =>
                new AssetService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton(sp =>
                new AvatarService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton<GameProcessService>();
            services.AddSingleton<IGameProcessService>(sp => sp.GetRequiredService<GameProcessService>());

            services.AddSingleton(sp =>
                new FileService(sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IFileService>(sp => sp.GetRequiredService<FileService>());

            services.AddSingleton(sp =>
                new UpdateService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<VersionService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<BrowserService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
            services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<UpdateService>());

            // New decomposed services
            services.AddSingleton(sp =>
                new PatchManager(
                    sp.GetRequiredService<IVersionService>(),
                    sp.GetRequiredService<IButlerService>(),
                    sp.GetRequiredService<IDownloadService>(),
                    sp.GetRequiredService<IInstanceService>(),
                    sp.GetRequiredService<IProgressNotificationService>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IPatchManager>(sp => sp.GetRequiredService<PatchManager>());

            services.AddSingleton(sp =>
                new GameLauncher(
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<ILaunchService>(),
                    sp.GetRequiredService<IInstanceService>(),
                    sp.GetRequiredService<IGameProcessService>(),
                    sp.GetRequiredService<IProgressNotificationService>(),
                    sp.GetRequiredService<IDiscordService>(),
                    sp.GetRequiredService<ISkinService>(),
                    sp.GetRequiredService<IUserIdentityService>(),
                    sp.GetRequiredService<AvatarService>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<HytaleAuthService>(),
                    sp.GetRequiredService<GpuDetectionService>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IGameLauncher>(sp => sp.GetRequiredService<GameLauncher>());

            services.AddSingleton(sp =>
                new GameSessionService(
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<IInstanceService>(),
                    sp.GetRequiredService<IVersionService>(),
                    sp.GetRequiredService<ILaunchService>(),
                    sp.GetRequiredService<IButlerService>(),
                    sp.GetRequiredService<IDownloadService>(),
                    sp.GetRequiredService<IProgressNotificationService>(),
                    sp.GetRequiredService<IPatchManager>(),
                    sp.GetRequiredService<IGameLauncher>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IGameSessionService>(sp => sp.GetRequiredService<GameSessionService>());

            #endregion

            #region User & Skin Management

            services.AddSingleton(sp =>
                new SkinService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>()));
            services.AddSingleton<ISkinService>(sp => sp.GetRequiredService<SkinService>());

            services.AddSingleton<UserIdentityService>();
            services.AddSingleton<IUserIdentityService>(sp => sp.GetRequiredService<UserIdentityService>());

            services.AddSingleton(sp =>
                new ProfileManagementService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<SkinService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<UserIdentityService>()));
            services.AddSingleton<IProfileManagementService>(sp => sp.GetRequiredService<ProfileManagementService>());

            services.AddSingleton(sp =>
                new HytaleAuthService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IBrowserService>(),
                    sp.GetRequiredService<ConfigService>()));
            services.AddSingleton<IHytaleAuthService>(sp => sp.GetRequiredService<HytaleAuthService>());

            // Version Sources — official source (requires auth)
            services.AddSingleton(sp =>
                new HytaleVersionSource(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<HytaleAuthService>(),
                    sp.GetRequiredService<IConfigService>()));

            // Mirror sources are loaded from JSON meta files by MirrorLoaderService
            // (see VersionService registration above)

            #endregion

            #region Localization & UI Support

            services.AddSingleton<LocalizationService>();
            services.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());

            services.AddSingleton(sp =>
                new ProgressNotificationService(sp.GetRequiredService<DiscordService>()));
            services.AddSingleton<IProgressNotificationService>(sp => sp.GetRequiredService<ProgressNotificationService>());

            services.AddSingleton<BrowserService>();
            services.AddSingleton<IBrowserService>(sp => sp.GetRequiredService<BrowserService>());

            services.AddSingleton<DiscordService>();
            services.AddSingleton<IDiscordService>(sp => sp.GetRequiredService<DiscordService>());

            services.AddSingleton<RosettaService>();

            services.AddSingleton<FileDialogService>();
            services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());

            services.AddSingleton(sp =>
                new ButlerService(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IButlerService>(sp => sp.GetRequiredService<ButlerService>());

            services.AddSingleton<GpuDetectionService>();

            services.AddSingleton(sp =>
                new SettingsService(
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<LocalizationService>()));
            services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());

            services.AddSingleton<ThemeService>();
            services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());

            services.AddSingleton<ClipboardService>();
            services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<ClipboardService>());

            #endregion

            #region IPC Bridge

            // IpcService needs all other services → receives IServiceProvider
            services.AddSingleton<IpcService>();

            #endregion

            var provider = services.BuildServiceProvider();
            Logger.Success("Bootstrapper", "Application services initialized successfully");

            return provider;
        }
        catch (Exception ex)
        {
            Logger.Error("Bootstrapper", $"Failed to initialize application services: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Performs async initialization tasks after DI container is built.
    /// Ensures CurseForge API key is available, fetching if needed
    /// </summary>
    /// <param name="services">The service provider.</param>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await EnsureCurseForgeKeyAsync(services);
    }
    
    /// <summary>
    /// Ensures the CurseForge API key is present in configuration.
    /// If missing, fetches it if needed.
    /// </summary>
    private static async Task EnsureCurseForgeKeyAsync(IServiceProvider services)
    {
        var configService = services.GetRequiredService<ConfigService>();
        var httpClient = services.GetRequiredService<HttpClient>();
        
        if (!string.IsNullOrEmpty(configService.Configuration.CurseForgeKey))
        {
            Logger.Info("Bootstrapper", "CurseForge API key already configured");
            return;
        }
        
        Logger.Info("Bootstrapper", "CurseForge API key not found, fetching...");
        
        try
        {
            var cmakeContent = await httpClient.GetStringAsync(CurseForgeKeySourceUrl);
            
            var match = Regex.Match(cmakeContent, @"set\(Launcher_CURSEFORGE_API_KEY\s+""([^""]+)""");
            
            if (match.Success)
            {
                var apiKey = match.Groups[1].Value;
                configService.Configuration.CurseForgeKey = apiKey;
                configService.SaveConfig();
                Logger.Success("Bootstrapper", "CurseForge API key fetched and saved successfully");
            }
            else
            {
                Logger.Warning("Bootstrapper", "Could not parse CurseForge API key");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Bootstrapper", $"Failed to fetch CurseForge API key: {ex.Message}");
        }
    }
}

// Simple wrapper to inject just the string path cleanly
public record AppPathConfiguration(string AppDir);
