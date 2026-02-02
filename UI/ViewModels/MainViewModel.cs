using ReactiveUI;
using System.Reactive;
using HyPrism.Backend;
using HyPrism.Backend.Services.Core;
using HyPrism.UI.Models;
using System.Threading.Tasks;
using System;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;

namespace HyPrism.UI.ViewModels;

public class MainViewModel : ReactiveObject
{
    public AppService AppService { get; }
    public LocalizationService Localization => AppService.Localization;

    // User Profile
    private string _nick;
    public string Nick
    {
        get => _nick;
        set => this.RaiseAndSetIfChanged(ref _nick, value);
    }
    
    // Status
    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => this.RaiseAndSetIfChanged(ref _isGameRunning, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    private double _progressValue; // 0-100
    public double ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private string _progressText = "Ready";
    public string ProgressText
    {
        get => _progressText;
        set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    private string _downloadSpeedText = "";
    public string DownloadSpeedText
    {
        get => _downloadSpeedText;
        set => this.RaiseAndSetIfChanged(ref _downloadSpeedText, value);
    }
    
    private string _progressIconPath = "/Assets/Icons/download-cloud.svg";
    public string ProgressIconPath
    {
        get => _progressIconPath;
        set => this.RaiseAndSetIfChanged(ref _progressIconPath, value);
    }
    
    private double _overlayOpacity = 1.0;
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
    }
    
    // Versioning
    private string _selectedBranch = "Release";
    public string SelectedBranch
    {
        get => _selectedBranch;
        set => this.RaiseAndSetIfChanged(ref _selectedBranch, value);
    }
    
    private int _selectedVersion = 0;
    public int SelectedVersion
    {
        get => _selectedVersion;
        set => this.RaiseAndSetIfChanged(ref _selectedVersion, value);
    }

    // Overlays
    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => this.RaiseAndSetIfChanged(ref _isSettingsOpen, value);
    }

    private bool _isConfigOpen; // For ModManager or others
    public bool IsModsOpen
    {
        get => _isConfigOpen;
        set => this.RaiseAndSetIfChanged(ref _isConfigOpen, value);
    }
    
    private bool _isProfileEditorOpen;
    public bool IsProfileEditorOpen
    {
        get => _isProfileEditorOpen;
        set => this.RaiseAndSetIfChanged(ref _isProfileEditorOpen, value);
    }
    
    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
    
    private double _mainContentOpacity = 0;
    public double MainContentOpacity
    {
        get => _mainContentOpacity;
        set => this.RaiseAndSetIfChanged(ref _mainContentOpacity, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isOverlayOpen;
    public bool IsOverlayOpen => _isOverlayOpen.Value;
    
    // Child ViewModels for Overlays
    private LoadingViewModel? _loadingViewModel;
    public LoadingViewModel? LoadingViewModel
    {
        get => _loadingViewModel;
        set => this.RaiseAndSetIfChanged(ref _loadingViewModel, value);
    }
    
    private SettingsViewModel? _settingsViewModel;
    public SettingsViewModel? SettingsViewModel
    {
        get => _settingsViewModel;
        set => this.RaiseAndSetIfChanged(ref _settingsViewModel, value);
    }

    private ModManagerViewModel? _modManagerViewModel;
    public ModManagerViewModel? ModManagerViewModel
    {
        get => _modManagerViewModel;
        set => this.RaiseAndSetIfChanged(ref _modManagerViewModel, value);
    }
    
    private ProfileEditorViewModel? _profileEditorViewModel;
    public ProfileEditorViewModel? ProfileEditorViewModel
    {
        get => _profileEditorViewModel;
        set => this.RaiseAndSetIfChanged(ref _profileEditorViewModel, value);
    }
    
    private NewsViewModel? _newsViewModel;
    public NewsViewModel? NewsViewModel
    {
        get => _newsViewModel;
        set => this.RaiseAndSetIfChanged(ref _newsViewModel, value);
    }
    
    // Error Modal
    private bool _isErrorModalOpen;
    public bool IsErrorModalOpen
    {
        get => _isErrorModalOpen;
        set => this.RaiseAndSetIfChanged(ref _isErrorModalOpen, value);
    }
    
    private string _errorTitle = "";
    public string ErrorTitle
    {
        get => _errorTitle;
        set => this.RaiseAndSetIfChanged(ref _errorTitle, value);
    }
    
    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    private string _errorTrace = "";
    public string ErrorTrace
    {
        get => _errorTrace;
        set => this.RaiseAndSetIfChanged(ref _errorTrace, value);
    }

    public ObservableCollection<string> Branches { get; } = new() { "Release", "Pre-Release" };
    
    // News
    public ObservableCollection<NewsItem> News { get; } = new();
    
    private bool _isLoadingNews;
    public bool IsLoadingNews
    {
        get => _isLoadingNews;
        set => this.RaiseAndSetIfChanged(ref _isLoadingNews, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleModsCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleProfileEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshNewsCommand { get; }
    public ReactiveCommand<string, Unit> OpenNewsLinkCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseErrorModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyErrorCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleMusicCommand { get; }
    
    public MainViewModel()
    {
        // Initialize loading screen
        LoadingViewModel = new LoadingViewModel();
        
        AppService = new AppService();
        _nick = AppService.Configuration.Nick;
        
        // Output properties
        _isOverlayOpen = this.WhenAnyValue(
                x => x.IsSettingsOpen, 
                x => x.IsModsOpen, 
                x => x.IsProfileEditorOpen,
                x => x.IsErrorModalOpen,
                (s, m, p, e) => s || m || p || e)
            .ToProperty(this, x => x.IsOverlayOpen);
        
        // Initialize child VMs
        SettingsViewModel = new SettingsViewModel(AppService);
        SettingsViewModel.CloseCommand.Subscribe(_ => IsSettingsOpen = false);
        
        NewsViewModel = new NewsViewModel(AppService);

        // Commands
        LaunchCommand = ReactiveCommand.CreateFromTask(LaunchAsync);
        OpenFolderCommand = ReactiveCommand.Create(() => 
        {
            var branch = SelectedBranch?.ToLower().Replace(" ", "-") ?? "release";
            AppService.OpenInstanceFolder(branch, SelectedVersion);
        });
        
        ToggleSettingsCommand = ReactiveCommand.Create(() => 
        {
            IsSettingsOpen = !IsSettingsOpen;
            if (IsSettingsOpen)
            {
                IsModsOpen = false;
                IsProfileEditorOpen = false;
            }
        });
        
        ToggleModsCommand = ReactiveCommand.Create(() => 
        {
            if (!IsModsOpen)
            {
                var branch = SelectedBranch?.ToLower().Replace(" ", "-") ?? "release"; 
                ModManagerViewModel = new ModManagerViewModel(AppService, branch, SelectedVersion);
                ModManagerViewModel.CloseCommand.Subscribe(_ => IsModsOpen = false);
            }
            IsModsOpen = !IsModsOpen;
            if (IsModsOpen)
            {
                IsSettingsOpen = false;
                IsProfileEditorOpen = false;
            }
        });
        
        ToggleProfileEditorCommand = ReactiveCommand.CreateFromTask(async () => 
        {
            if (!IsProfileEditorOpen)
            {
                ProfileEditorViewModel = new ProfileEditorViewModel(AppService);
                // Close command handling
                var closeCmd = ProfileEditorViewModel.CloseCommand as ReactiveCommand<Unit, Unit>;
                closeCmd?.Subscribe(_ => IsProfileEditorOpen = false);
                // Profile update handling
                ProfileEditorViewModel.ProfileUpdated += () => Nick = AppService.Configuration.Nick;
                await ProfileEditorViewModel.LoadProfileAsync();
            }
            IsProfileEditorOpen = !IsProfileEditorOpen;
            if (IsProfileEditorOpen)
            {
                IsSettingsOpen = false;
                IsModsOpen = false;
            }
        });
        
        RefreshNewsCommand = ReactiveCommand.CreateFromTask(LoadNewsAsync);
        OpenNewsLinkCommand = ReactiveCommand.Create<string>(url => 
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch { }
            }
        });
        
        CloseErrorModalCommand = ReactiveCommand.Create(() => 
        {
            IsErrorModalOpen = false;
        });
        CopyErrorCommand = ReactiveCommand.Create(() =>
        {
            var errorText = $"Error: {ErrorTitle}\nMessage: {ErrorMessage}\nTrace:\n{ErrorTrace}";
            // TODO: Copy to clipboard (Avalonia clipboard API)
            return Unit.Default;
        });
        
        ToggleMusicCommand = ReactiveCommand.Create(ToggleMusic);
        
        // Event subscriptions need to be marshaled to UI thread
        AppService.DownloadProgressChanged += (state, progress, speed, dl, total) => 
            Dispatcher.UIThread.Post(() => OnDownloadProgress(state, progress, speed, dl, total));
            
        AppService.GameStateChanged += (state, code) => 
            Dispatcher.UIThread.Post(() => OnGameStateChanged(state, code));
            
        AppService.ErrorOccurred += (title, msg, trace) => 
            Dispatcher.UIThread.Post(() => OnError(title, msg, trace));
        
        // Initialize application data asynchronously
        _ = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        try
        {
            // Load initial news and music state
            await LoadNewsAsync();
            await LoadMusicStateAsync();
            
            // Simulate minimum loading time for smooth UX
            await Task.Delay(3000);
            
            // Complete loading screen animation
            if (LoadingViewModel != null)
            {
                await LoadingViewModel.CompleteLoadingAsync();
            }
            
            // Hide loading screen and fade in main content simultaneously
            IsLoading = false;
            MainContentOpacity = 1;
        }
        catch (Exception ex)
        {
            Logger.Error("MainViewModel", $"Initialization error: {ex.Message}");
            // Still hide loading screen even if there's an error
            IsLoading = false;
        }
    }

    private async void OnDownloadProgress(string state, double progress, string speed, long downloaded, long total)
    {
        IsDownloading = true;
        ProgressValue = progress; // Already in 0-100 range
        
        // Reset opacity when showing overlay
        if (OverlayOpacity < 1.0)
            OverlayOpacity = 1.0;
        
        // Change icon based on state
        var stateLower = state.ToLower();
        
        // Handle "complete" state - show success message and hide overlay
        if (stateLower == "complete")
        {
            ProgressIconPath = "/Assets/Icons/smile.svg";
            ProgressText = "Launched!";
            ProgressValue = 100;
            DownloadSpeedText = "";
            
            // Wait 4 seconds
            await Task.Delay(4000);
            
            // Fade out
            OverlayOpacity = 0;
            await Task.Delay(500); // Wait for fade animation
            
            // Hide overlay
            IsDownloading = false;
            ProgressValue = 0;
            return;
        }
        
        // Normal state handling
        ProgressText = state;
        DownloadSpeedText = speed;
        
        if (stateLower.Contains("download") || stateLower.Contains("загрузк"))
            ProgressIconPath = "/Assets/Icons/download-cloud.svg";
        else if (stateLower.Contains("patch") || stateLower.Contains("патч") || stateLower.Contains("extract") || stateLower.Contains("распак"))
            ProgressIconPath = "/Assets/Icons/wrench.svg";
        else if (stateLower.Contains("launch") || stateLower.Contains("запуск") || stateLower.Contains("start"))
            ProgressIconPath = "/Assets/Icons/rocket.svg";
    }

    private async void OnGameStateChanged(string state, int code)
    {
        // Adjust logic based on actual AppService notification codes
        // Assuming code > 0 is running state or similar
        var stateLower = state.ToLower();
        IsGameRunning = stateLower.Contains("running") || stateLower.Contains("playing");
        
        if (IsGameRunning)
        {
            // Show success animation before hiding
            ProgressIconPath = "/Assets/Icons/smile.svg";
            ProgressText = "Launched!";
            ProgressValue = 100;
            DownloadSpeedText = "";
            
            // Wait 4 seconds
            await Task.Delay(4000);
            
            // Fade out
            OverlayOpacity = 0;
            await Task.Delay(500); // Wait for fade animation
            
            // Hide overlay
            IsDownloading = false;
            ProgressValue = 0;
        }
        else if (!IsDownloading)
        {
            ProgressValue = 0;
            DownloadSpeedText = "";
        }
    }

    private void OnError(string title, string message, string? trace)
    {
         ProgressText = $"Error: {message}";
         IsDownloading = false;
         
         // Show error modal
         ErrorTitle = title;
         ErrorMessage = message;
         ErrorTrace = trace ?? "";
         IsErrorModalOpen = true;
    }

    private async Task LaunchAsync()
    {
        if (IsGameRunning) return;
        
        try 
        {
            // Reset state
            IsDownloading = true;
            ProgressText = "Preparing...";
            
            await AppService.DownloadAndLaunchAsync();
        }
        catch (Exception ex)
        {
            OnError("Launch Error", ex.Message, ex.StackTrace);
        }
        finally
        {
             // If game started, logic is handled by event. 
        }
    }
    
    private async Task LoadNewsAsync()
    {
        if (AppService.Configuration.DisableNews) return;
        
        IsLoadingNews = true;
        try
        {
            var newsItems = await AppService.GetNewsAsync(10);
            
            Dispatcher.UIThread.Post(() =>
            {
                News.Clear();
                if (newsItems != null)
                {
                    foreach (var item in newsItems)
                    {
                        News.Add(new NewsItem
                        {
                            Title = item.Title,
                            Excerpt = item.Excerpt,
                            Url = item.Url ?? "",
                            Date = item.Date,
                            Author = item.Author,
                            ImageUrl = item.ImageUrl ?? "",
                            Source = "hytale"
                        });
                    }
                }
            });
        }
        catch
        {
            // Ignore news loading errors
        }
        finally
        {
            IsLoadingNews = false;
        }
    }
    
    // Music Player
    private bool _isMusicEnabled;
    public bool IsMusicEnabled
    {
        get => _isMusicEnabled;
        set => this.RaiseAndSetIfChanged(ref _isMusicEnabled, value);
    }
    
    private double _volumeScale = 1.0;
    public double VolumeScale
    {
        get => _volumeScale;
        set => this.RaiseAndSetIfChanged(ref _volumeScale, value);
    }
    
    public string MusicTooltip => IsMusicEnabled ? "Music: ON" : "Music: OFF";
    
    private void ToggleMusic()
    {
        IsMusicEnabled = !IsMusicEnabled;
        AppService.SetMusicEnabled(IsMusicEnabled);
        
        // Animate volume icon
        if (IsMusicEnabled)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Dispatcher.UIThread.Post(() => VolumeScale = 1.2);
                    await Task.Delay(150);
                    Dispatcher.UIThread.Post(() => VolumeScale = 1.0);
                    await Task.Delay(150);
                }
            });
        }
        
        this.RaisePropertyChanged(nameof(MusicTooltip));
    }
    
    private async Task LoadMusicStateAsync()
    {
        IsMusicEnabled = await AppService.GetMusicEnabledAsync();
    }
}
