using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Services.User;
using HyPrism.Services.Game;
using HyPrism.UI.MainWindow;
using HyPrism.UI.Views.NewsView;
using HyPrism.UI.Views.SettingsView;
using HyPrism.UI.Views.ModManagerView;
using HyPrism.UI.Views.ProfileEditorView;

namespace HyPrism;

public static class Bootstrapper
{
    public static IServiceProvider Initialize()
    {
        Logger.Info("Bootstrapper", "Initializing application services...");
        try
        {
            var services = new ServiceCollection();

            #region Core Infrastructure & Configuration

            var appDir = UtilityService.GetEffectiveAppDir();
            services.AddSingleton(new AppPathConfiguration(appDir));

            services.AddSingleton(_ => new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            });

            // Config
            services.AddSingleton<ConfigService>(sp =>
                new ConfigService(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IConfigService>(sp => sp.GetRequiredService<ConfigService>());

            #endregion

            #region Data & Utility Services

            services.AddSingleton<NewsService>();
            services.AddSingleton<INewsService>(sp => sp.GetRequiredService<NewsService>());

            services.AddSingleton<ProfileService>(sp =>
                new ProfileService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>()));
            services.AddSingleton<IProfileService>(sp => sp.GetRequiredService<ProfileService>());

            services.AddSingleton<DownloadService>();
            services.AddSingleton<IDownloadService>(sp => sp.GetRequiredService<DownloadService>());

            services.AddSingleton<GitHubService>(sp =>
                new GitHubService(sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IGitHubService>(sp => sp.GetRequiredService<GitHubService>());

            services.AddSingleton<VersionService>(sp =>
                new VersionService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<IConfigService>()));
            services.AddSingleton<IVersionService>(sp => sp.GetRequiredService<VersionService>());

            #endregion

            #region Game & Instance Management

            services.AddSingleton<InstanceService>(sp =>
                new InstanceService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>()));
            services.AddSingleton<IInstanceService>(sp => sp.GetRequiredService<InstanceService>());

            services.AddSingleton<ModService>(sp =>
                new ModService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
            services.AddSingleton<IModService>(sp => sp.GetRequiredService<ModService>());

            services.AddSingleton<LaunchService>(sp =>
                new LaunchService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<ILaunchService>(sp => sp.GetRequiredService<LaunchService>());

            services.AddSingleton<AssetService>(sp =>
                new AssetService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton<AvatarService>(sp =>
                new AvatarService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton<GameProcessService>();
            services.AddSingleton<IGameProcessService>(sp => sp.GetRequiredService<GameProcessService>());

            services.AddSingleton<FileService>(sp =>
                new FileService(sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IFileService>(sp => sp.GetRequiredService<FileService>());

            services.AddSingleton<UpdateService>(sp =>
                new UpdateService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<VersionService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<BrowserService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
            services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<UpdateService>());

            // New decomposed services
            services.AddSingleton<PatchManager>(sp =>
                new PatchManager(
                    sp.GetRequiredService<IVersionService>(),
                    sp.GetRequiredService<IButlerService>(),
                    sp.GetRequiredService<IDownloadService>(),
                    sp.GetRequiredService<IInstanceService>(),
                    sp.GetRequiredService<IProgressNotificationService>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IPatchManager>(sp => sp.GetRequiredService<PatchManager>());

            services.AddSingleton<GameLauncher>(sp =>
                new GameLauncher(
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<ILaunchService>(),
                    sp.GetRequiredService<IInstanceService>(),
                    sp.GetRequiredService<IGameProcessService>(),
                    sp.GetRequiredService<IProgressNotificationService>(),
                    sp.GetRequiredService<IDiscordService>(),
                    sp.GetRequiredService<ISkinService>(),
                    sp.GetRequiredService<IUserIdentityService>(),
                    sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IGameLauncher>(sp => sp.GetRequiredService<GameLauncher>());

            services.AddSingleton<GameSessionService>(sp =>
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

            services.AddSingleton<SkinService>(sp =>
                new SkinService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>()));
            services.AddSingleton<ISkinService>(sp => sp.GetRequiredService<SkinService>());

            services.AddSingleton<UserIdentityService>();
            services.AddSingleton<IUserIdentityService>(sp => sp.GetRequiredService<UserIdentityService>());

            services.AddSingleton<ProfileManagementService>(sp =>
                new ProfileManagementService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<SkinService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<UserIdentityService>()));
            services.AddSingleton<IProfileManagementService>(sp => sp.GetRequiredService<ProfileManagementService>());

            #endregion

            #region Localization & UI Support

            services.AddSingleton<LanguageService>();

            services.AddSingleton<LocalizationService>();
            services.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());

            services.AddSingleton<ProgressNotificationService>(sp =>
                new ProgressNotificationService(sp.GetRequiredService<DiscordService>()));
            services.AddSingleton<IProgressNotificationService>(sp => sp.GetRequiredService<ProgressNotificationService>());

            services.AddSingleton<BrowserService>();
            services.AddSingleton<IBrowserService>(sp => sp.GetRequiredService<BrowserService>());

            services.AddSingleton<DiscordService>();
            services.AddSingleton<IDiscordService>(sp => sp.GetRequiredService<DiscordService>());

            services.AddSingleton<RosettaService>();

            services.AddSingleton<FileDialogService>();
            services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());

            services.AddSingleton<ButlerService>(sp =>
                new ButlerService(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IButlerService>(sp => sp.GetRequiredService<ButlerService>());

            services.AddSingleton<SettingsService>(sp =>
                new SettingsService(
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<LocalizationService>()));
            services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());

            services.AddSingleton<ThemeService>();
            services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());

            services.AddSingleton<ClipboardService>();
            services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<ClipboardService>());

            #endregion

            #region ViewModels

            services.AddSingleton<MainViewModel>();

            services.AddTransient<NewsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ModManagerViewModel>();
            services.AddTransient<ProfileEditorViewModel>();

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
}

// Simple wrapper to inject just the string path cleanly
public record AppPathConfiguration(string AppDir);
