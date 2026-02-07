using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Services.Game;
using HyPrism.Services.User;
using HyPrism;
using HyPrism.UI.Views.LoadingView;
using HyPrism.UI.Views.DashboardView;

namespace HyPrism.UI.MainWindow;

public class MainViewModel : ReactiveObject, IDisposable
{
    private readonly NewsService _newsService;
    private readonly DiscordService _discordService;
    private readonly ProfileManagementService _profileManagementService;
    private readonly InstanceService _instanceService;
    private readonly SkinService _skinService;
    private readonly VersionService _versionService;
    private readonly ConfigService _configService;
    private bool _disposed;

    // --- Core Architecture ---
    // The Loading Screen is distinct and effectively "covers" the application
    public LoadingViewModel LoadingViewModel { get; set; }

    // The Dashboard contains all the main application UI (Header, Game Controls, News, Overlays)
    public DashboardViewModel DashboardViewModel { get; private set; }

    // --- State Observables for Window Host ---
    private readonly ObservableAsPropertyHelper<bool> _isLoading;
    public bool IsLoading => _isLoading.Value;    
    
    // Controls the opacity of the Dashboard container based on loading state
    private readonly ObservableAsPropertyHelper<double> _mainContentOpacity;
    public double MainContentOpacity => _mainContentOpacity.Value;

    public bool DisableNews => true; // Moved logic to dashboard, stub for init

    public MainViewModel(
        DiscordService discordService,
        ProfileManagementService profileManagementService,
        VersionService versionService,
        // We inject all services to pass them down to the Dashboard
        // Ideally we would use a Factory or DI container resolution for the child, 
        // but passing them is fine for now.
        GameSessionService gameSessionService,
        GameProcessService gameProcessService,
        ModService modService,
        InstanceService instanceService,
        ConfigService configService,
        FileService fileService,
        ProgressNotificationService progressService,
        BrowserService browserService,
        NewsService newsService,
        SettingsService settingsService,
        FileDialogService fileDialogService,
        ProfileService profileService,
        SkinService skinService,
        GitHubService gitHubService,
        AppPathConfiguration appPathConfiguration,
        LocalizationService localizationService,
        IClipboardService clipboardService)
    {
        _newsService = newsService;
        _discordService = discordService;
        _profileManagementService = profileManagementService;
        _instanceService = instanceService;
        _skinService = skinService;
        _versionService = versionService;
        _configService = configService;

        LoadingViewModel = new LoadingViewModel();
        
        // Output properties
        _isLoading = this.WhenAnyValue(x => x.LoadingViewModel.IsLoading)
            .ToProperty(this, x => x.IsLoading, scheduler: RxApp.MainThreadScheduler);
            
        // Use scheduler to ensure UI thread update
        _mainContentOpacity = this.WhenAnyValue(x => x.LoadingViewModel.IsLoading)
            .Select(isLoading => isLoading ? 0.0 : 1.0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.MainContentOpacity, scheduler: RxApp.MainThreadScheduler);

        // Create the Dashboard ViewModel which encapsulates the main app logic
        DashboardViewModel = new DashboardViewModel(
            gameSessionService,
            gameProcessService,
            modService,
            instanceService,
            configService,
            versionService,
            fileService,
            progressService,
            browserService,
            newsService,
            settingsService,
            fileDialogService,
            profileService,
            skinService,
            gitHubService,
            appPathConfiguration,
            localizationService,
            clipboardService,
            profileManagementService
        );

        // Start App Initialization sequence (fire-and-forget with error handling)
        _ = InitializeAppAsync();
    }
    
    // --- Initialization ---

    private async Task InitializeAppAsync()
    {
        try
        {
            // Initial delay to let UI render frame
            await Task.Delay(100);

            // Startup Initialization (Formerly in AppService)
            _skinService.TryRecoverOrphanedSkinOnStartup();
            _instanceService.MigrateLegacyData();
            _discordService.Initialize();
            _profileManagementService.InitializeProfileModsSymlink();

            if (LoadingViewModel != null)
            {
                LoadingViewModel.Update("loading", "Loading Localization...", 20);
                await Task.Delay(300);

                // TODO: Read this setting from DashboardViewModel if possible or service
                // For now, assuming we want to load news
                bool disableNews = false; 

                if (!disableNews)
                {
                    LoadingViewModel.Update("loading", "Loading News...", 60);
                    // Fetch more items to populate cache for dashboard
                    var newsTask = _newsService.GetNewsAsync(30);
                    var timeoutTask = Task.Delay(3000); 
                    
                    await Task.WhenAny(newsTask, timeoutTask);
                }
                
                // Pre-cache versions for both branches
                LoadingViewModel.Update("loading", "Caching game versions...", 80);
                try
                {
                    var releaseCacheTask = _versionService.GetVersionListAsync("release");
                    var preReleaseCacheTask = _versionService.GetVersionListAsync("pre-release");
                    
                    var completedTask = await Task.WhenAny(
                        Task.WhenAll(releaseCacheTask, preReleaseCacheTask)
                    );
                }
                catch (Exception cacheEx)
                {
                    Logger.Error("Startup", $"Version caching failed: {cacheEx.Message}");
                }
                
                LoadingViewModel.Update("complete", "Ready!", 100);
                await Task.Delay(200);
            }
        }
        catch (Exception ex)
        {
             Services.Core.Logger.Error("Startup", $"Initialization error: {ex.Message}");
        }
        finally
        {
            if (LoadingViewModel != null)
            {
                 await LoadingViewModel.CompleteLoadingAsync();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _isLoading.Dispose();
        _mainContentOpacity.Dispose();
        
        (DashboardViewModel as IDisposable)?.Dispose();
    }
}
