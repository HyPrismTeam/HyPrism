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

            // Single Application Environment Path
            var appDir = UtilityService.GetEffectiveAppDir();
            services.AddSingleton(new AppPathConfiguration(appDir));
            
            // Global HttpClient
            services.AddSingleton(_ => new HttpClient 
            { 
                Timeout = TimeSpan.FromMinutes(30) 
            });

            // Config Service
            services.AddSingleton(sp => 
                new ConfigService(sp.GetRequiredService<AppPathConfiguration>().AppDir));

            #endregion

            #region Data & Utility Services

            services.AddSingleton<NewsService>(); 
            
            services.AddSingleton(sp => 
                new ProfileService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>()));
                    
            services.AddSingleton<DownloadService>(); 
            services.AddSingleton<GitHubService>();
            
            services.AddSingleton(sp => 
                new VersionService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<ConfigService>()));

            #endregion

            #region Game & Instance Management

            services.AddSingleton(sp =>
                new ModService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
                    
            services.AddSingleton(sp =>
                new LaunchService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>()));
                    
            services.AddSingleton(sp =>
                new InstanceService(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<ConfigService>()));

            services.AddSingleton(sp =>
                new AssetService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton(sp =>
                new AvatarService(
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton<GameProcessService>();

            services.AddSingleton(sp =>
                new FileService(sp.GetRequiredService<AppPathConfiguration>()));

            services.AddSingleton(sp =>
                new UpdateService(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<VersionService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<BrowserService>(),
                    sp.GetRequiredService<ProgressNotificationService>()));
            
            services.AddSingleton(sp =>
                new GameSessionService(
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<VersionService>(),
                    sp.GetRequiredService<UpdateService>(),
                    sp.GetRequiredService<LaunchService>(),
                    sp.GetRequiredService<ButlerService>(),
                    sp.GetRequiredService<DownloadService>(),
                    sp.GetRequiredService<ModService>(),
                    sp.GetRequiredService<SkinService>(),
                    sp.GetRequiredService<UserIdentityService>(),
                    sp.GetRequiredService<GameProcessService>(),
                    sp.GetRequiredService<ProgressNotificationService>(),
                    sp.GetRequiredService<DiscordService>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));

            #endregion
            
            #region User & Skin Management

            services.AddSingleton(sp =>
                new SkinService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<InstanceService>()));

            services.AddSingleton<UserIdentityService>();

            services.AddSingleton(sp =>
                new ProfileManagementService(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<ConfigService>(),
                    sp.GetRequiredService<SkinService>(),
                    sp.GetRequiredService<InstanceService>(),
                    sp.GetRequiredService<UserIdentityService>()));

            #endregion

            #region Localization & UI Support

            services.AddSingleton<LanguageService>();
            services.AddSingleton(sp => LocalizationService.Instance);
            
            services.AddSingleton(sp =>
                new ProgressNotificationService(sp.GetRequiredService<DiscordService>()));
            
            services.AddSingleton<BrowserService>();
            services.AddSingleton<DiscordService>();
            services.AddSingleton<RosettaService>();
            services.AddSingleton<FileDialogService>();
            services.AddSingleton(sp =>
                new ButlerService(sp.GetRequiredService<AppPathConfiguration>().AppDir));

            services.AddSingleton<SettingsService>();

            #endregion

            #region Legacy AppService

            // The "God Object" AppService - eventually to be removed
            // REFACTORED: AppService removed. ViewModels now inject specific services.
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
