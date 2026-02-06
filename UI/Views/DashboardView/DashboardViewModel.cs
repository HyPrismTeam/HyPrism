using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using HyPrism;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Services.Game;
using HyPrism.Services.User;
using HyPrism.Models;
using Avalonia.Threading;
using HyPrism.UI.Components.Dashboard.GameControlBar;
using HyPrism.UI.Components.Dashboard.Header;
using HyPrism.UI.Views.NewsView;
using HyPrism.UI.Views.SettingsView;
using HyPrism.UI.Views.ProfileEditorView;
using HyPrism.UI.Views.ModManagerView;
using Microsoft.Extensions.DependencyInjection;
using HyPrism.UI.Helpers;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.IO;

namespace HyPrism.UI.Views.DashboardView;

public class DashboardViewModel : ReactiveObject
{
    private readonly GameSessionService _gameSessionService;
    private readonly GameProcessService _gameProcessService;
    private readonly ModService _modService;
    private readonly InstanceService _instanceService;
    private readonly ConfigService _configService;
    private readonly VersionService _versionService;
    private readonly FileService _fileService;
    private readonly ProgressNotificationService _progressService;
    private readonly BrowserService _browserService;
    private readonly NewsService _newsService;
    private readonly SettingsService _settingsService;
    private readonly FileDialogService _fileDialogService;
    private readonly ProfileService _profileService;
    private readonly SkinService _skinService;
    private readonly AppPathConfiguration _appPathConfiguration;

    // Partial ViewModels
    public HeaderViewModel HeaderViewModel { get; }
    public GameControlViewModel GameControlViewModel { get; }
    public NewsViewModel NewsViewModel { get; }

    // Child ViewModels (Lazy Loaded)
    private SettingsViewModel? _settingsViewModel;
    public SettingsViewModel? SettingsViewModel
    {
        get => _settingsViewModel;
        set => this.RaiseAndSetIfChanged(ref _settingsViewModel, value);
    }
    
    private ProfileEditorViewModel? _profileEditorViewModel;
    public ProfileEditorViewModel? ProfileEditorViewModel
    {
        get => _profileEditorViewModel;
        set => this.RaiseAndSetIfChanged(ref _profileEditorViewModel, value);
    }
    
    private ModManagerViewModel? _modManagerViewModel;
    public ModManagerViewModel? ModManagerViewModel
    {
        get => _modManagerViewModel;
        set => this.RaiseAndSetIfChanged(ref _modManagerViewModel, value);
    }

    // Overlay State
    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set 
        {
            if (_isSettingsOpen != value)
            {
                this.RaiseAndSetIfChanged(ref _isSettingsOpen, value);
                if (value)
                {
                    _isConfigOpen = false;
                    this.RaisePropertyChanged(nameof(IsModsOpen));
                    _isProfileEditorOpen = false;
                    this.RaisePropertyChanged(nameof(IsProfileEditorOpen));
                }
            }
        }
    }
    
    private bool _isConfigOpen; // For ModManager
    public bool IsModsOpen
    {
        get => _isConfigOpen;
        set
        {
            if (_isConfigOpen != value)
            {
                _isConfigOpen = value;
                this.RaisePropertyChanged();
                if (value)
                {
                    _isSettingsOpen = false;
                    this.RaisePropertyChanged(nameof(IsSettingsOpen));
                    _isProfileEditorOpen = false;
                    this.RaisePropertyChanged(nameof(IsProfileEditorOpen));
                }
            }
        }
    }
    
    private bool _isProfileEditorOpen;
    public bool IsProfileEditorOpen
    {
        get => _isProfileEditorOpen;
        set
        {
            if (_isProfileEditorOpen != value)
            {
                _isProfileEditorOpen = value;
                this.RaisePropertyChanged();
                if (value)
                {
                    _isSettingsOpen = false;
                    this.RaisePropertyChanged(nameof(IsSettingsOpen));
                    _isConfigOpen = false;
                    this.RaisePropertyChanged(nameof(IsModsOpen));
                }
            }
        }
    }

    private bool _isErrorModalOpen;
    public bool IsErrorModalOpen
    {
        get => _isErrorModalOpen;
        set => this.RaiseAndSetIfChanged(ref _isErrorModalOpen, value);
    }
    
    // Background Logic
    private Bitmap? _backgroundImage;
    public Bitmap? BackgroundImage
    {
        get => _backgroundImage;
        set => this.RaiseAndSetIfChanged(ref _backgroundImage, value);
    }

    private double _backgroundOpacity = 1.0;
    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set => this.RaiseAndSetIfChanged(ref _backgroundOpacity, value);
    }

    
    private DispatcherTimer? _backgroundTimer;
    private int _currentSlideIndex = 0;
    private List<string> _slideshowImages = new();

    private readonly ObservableAsPropertyHelper<bool> _isOverlayOpen;
    public bool IsOverlayOpen => _isOverlayOpen.Value;

    // Launch Overlay Properties
    private bool _isLaunchOverlayVisible;
    public bool IsLaunchOverlayVisible
    {
        get => _isLaunchOverlayVisible;
        set 
        {
            this.RaiseAndSetIfChanged(ref _isLaunchOverlayVisible, value);
            OverlayOpacity = value ? 1.0 : 0.0;
        }
    }

    private double _overlayOpacity;
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
    }
    
    private string _statusTitle = "Launching...";
    public string StatusTitle
    {
        get => _statusTitle;
        set => this.RaiseAndSetIfChanged(ref _statusTitle, value);
    }
    
    private string _progressText = "Please wait while we download and install the game files";
    public string ProgressText
    {
        get => _progressText;
        set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }
    
    private string _currentSpeed = "";
    public string CurrentSpeed
    {
        get => _currentSpeed;
        set => this.RaiseAndSetIfChanged(ref _currentSpeed, value);
    }
    
    private string _progressIconPath = "/Assets/Icons/download-cloud.svg";
    public string ProgressIconPath
    {
        get => _progressIconPath;
        set => this.RaiseAndSetIfChanged(ref _progressIconPath, value);
    }

    private bool _isLaunchCancelRequested;

    private bool _launchAfterDownload = true;
    public bool LaunchAfterDownload
    {
        get => _launchAfterDownload;
        set => this.RaiseAndSetIfChanged(ref _launchAfterDownload, value);
    }

    private bool _isLaunchAfterDownloadVisible;
    public bool IsLaunchAfterDownloadVisible
    {
        get => _isLaunchAfterDownloadVisible;
        set => this.RaiseAndSetIfChanged(ref _isLaunchAfterDownloadVisible, value);
    }

    private readonly ObservableAsPropertyHelper<string> _launchAfterDownloadLabel;
    public string LaunchAfterDownloadLabel => _launchAfterDownloadLabel.Value;

    private readonly ObservableAsPropertyHelper<string> _cancelLaunchLabel;
    public string CancelLaunchLabel => _cancelLaunchLabel.Value;

    // Error
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

    // Commands
    public ReactiveCommand<Unit, Unit> CloseErrorModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyErrorCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelLaunchCommand { get; }

    public DashboardViewModel(
        GameSessionService gameSessionService,
        GameProcessService gameProcessService,
        ModService modService,
        InstanceService instanceService,
        ConfigService configService,
        VersionService versionService,
        FileService fileService,
        ProgressNotificationService progressService,
        BrowserService browserService,
        NewsService newsService,
        SettingsService settingsService,
        FileDialogService fileDialogService,
        ProfileService profileService,
        SkinService skinService,
        GitHubService gitHubService,
        AppPathConfiguration appPathConfiguration)
    {
        _gameSessionService = gameSessionService;
        _gameProcessService = gameProcessService;
        _modService = modService;
        _instanceService = instanceService;
        _configService = configService;
        _versionService = versionService;
        _fileService = fileService;
        _progressService = progressService;
        _browserService = browserService;
        _newsService = newsService;
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _profileService = profileService;
        _skinService = skinService;
        _appPathConfiguration = appPathConfiguration;

        // Initialize Backgrounds
        try 
        {
            _slideshowImages = _settingsService.GetAvailableBackgrounds()
                .Select(x => $"avares://HyPrism/Assets/Images/Backgrounds/{x}")
                .ToList();
        } 
        catch (Exception ex)
        {
            Logger.Error("Dashboard", $"Error loading backgrounds: {ex.Message}");
        }
        
        var bgMode = _settingsService.GetBackgroundMode();
        if (bgMode == "slideshow") bgMode = "auto"; // Legacy compatibility
        UpdateBackground(bgMode);
        _settingsService.OnBackgroundChanged += UpdateBackground;

        // --- Setup Overlay State ---
        _isOverlayOpen = this.WhenAnyValue(
                x => x.IsSettingsOpen, 
                x => x.IsModsOpen, 
                x => x.IsProfileEditorOpen,
                x => x.IsErrorModalOpen,
                (s, m, p, e) => s || m || p || e)
            .ToProperty(this, x => x.IsOverlayOpen);

        _launchAfterDownloadLabel = LocalizationService.Instance
            .GetObservable("launch.option.runAfterDownload")
            .ToProperty(this, x => x.LaunchAfterDownloadLabel);

        _cancelLaunchLabel = LocalizationService.Instance
            .GetObservable("launch.option.cancel")
            .ToProperty(this, x => x.CancelLaunchLabel);

        // --- Setup Actions ---
        Action toggleSettingsAction = () => IsSettingsOpen = !IsSettingsOpen;
        Action openInstancesAction = OpenInstancesSettings;
        Action toggleProfileEditorAction = () => { _ = ToggleProfileEditorAsync(); };
        Action<string, int> toggleModsAction = (branchName, version) =>
        {
            if (!IsModsOpen)
            {
                var branch = branchName?.ToLower().Replace(" ", "-") ?? "release"; 
                ModManagerViewModel = new ModManagerViewModel(_modService, _instanceService, branch, version);
                ModManagerViewModel.CloseCommand.Subscribe(_ => IsModsOpen = false);
            }
            IsModsOpen = !IsModsOpen;
        };

        // --- Initialize Child ViewModels ---
        HeaderViewModel = new HeaderViewModel(_configService, toggleProfileEditorAction, toggleSettingsAction);
        GameControlViewModel = new GameControlViewModel(_instanceService, _fileService, _gameProcessService, _configService, _versionService, toggleModsAction, toggleSettingsAction, openInstancesAction, LaunchAsync);
        NewsViewModel = new NewsViewModel(_newsService, _browserService);
        
        // Lazy-load settings if possible, or init straight away
        // We'll init settings VM here to handle the lazy open
        SettingsViewModel = new SettingsViewModel(
            _settingsService, 
            _configService, 
            _fileDialogService, 
            LocalizationService.Instance,
            _instanceService,
            _fileService,
            gitHubService,
            _browserService,
            _appPathConfiguration,
            _versionService);
        SettingsViewModel.CloseCommand.Subscribe(_ => IsSettingsOpen = false);

        // --- Commands ---
        CloseErrorModalCommand = ReactiveCommand.Create(() => { IsErrorModalOpen = false; });
        CopyErrorCommand = ReactiveCommand.Create(() => 
        {
             // TODO: Clip
        });
           CancelLaunchCommand = ReactiveCommand.Create(CancelLaunch);

        // --- Subscriptions ---
        _progressService.DownloadProgressChanged += OnDownloadProgressChanged;
        _progressService.ErrorOccurred += OnErrorOccurred;
    }

    public bool DisableNews => _settingsService.GetDisableNews();

    private async Task ToggleProfileEditorAsync()
    {
        if (IsProfileEditorOpen)
        {
            IsProfileEditorOpen = false;
            return;
        }

        ProfileEditorViewModel = new ProfileEditorViewModel(_configService, _profileService, _skinService, _fileService);
        // Wire up close
        var closeCmd = ProfileEditorViewModel.CloseCommand as ReactiveCommand<Unit, Unit>;
        closeCmd?.Subscribe(_ => IsProfileEditorOpen = false);
        // Wire up update
        ProfileEditorViewModel.ProfileUpdated += () => HeaderViewModel.RefreshNick();

        await ProfileEditorViewModel.LoadProfileAsync();
        IsProfileEditorOpen = true;
    }

    private async Task LaunchAsync()
    {
        if (IsLaunchOverlayVisible) return;

        _isLaunchCancelRequested = false;
        
        // Reset state with localized values matching "preparing" event
        // This ensures smooth visual transition (no icon swap) when service starts
        DownloadProgress = 0;
        StatusTitle = LocalizationService.Instance.Translate("launch.state.preparing");
        if (string.IsNullOrEmpty(StatusTitle)) StatusTitle = "Preparing...";
        
        ProgressIconPath = "/Assets/Icons/settings.svg"; // Matches 'preparing' state icon
        ProgressText = LocalizationService.Instance.Translate("launch.detail.preparing_session");
        if (string.IsNullOrEmpty(ProgressText)) ProgressText = "Preparing game session...";

        IsLaunchAfterDownloadVisible = false;
        
        IsLaunchOverlayVisible = true;
        
        try
        {
            var result = await Task.Run(() => _gameSessionService.DownloadAndLaunchAsync(() => LaunchAfterDownload));
            
            if (result.Error != null)
            {
                if (_isLaunchCancelRequested || string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    IsLaunchOverlayVisible = false;
                    return;
                }
                IsLaunchOverlayVisible = false;
                OnErrorOccurred("launch", "Launch failed", result.Error);
                return;
            }
            
            if (result.Cancelled)
            {
                IsLaunchOverlayVisible = false;
                return;
            }

            // Wait 2 seconds to let user see the "Done" state before fading out
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            OnErrorOccurred("launch", "Launch failed", ex.ToString());
        }
        finally
        {
            IsLaunchOverlayVisible = false;
        }
    }

    private void OnDownloadProgressChanged(ProgressUpdateMessage msg)
    {
        Dispatcher.UIThread.InvokeAsync(() => {

            if (_isLaunchCancelRequested)
            {
                return;
            }
            
            // Set Icon
            ProgressIconPath = msg.State switch
            {
                "preparing" => "/Assets/Icons/settings.svg",
                "download" => "/Assets/Icons/download-cloud.svg",
                "update" => "/Assets/Icons/refresh-cw.svg",
                "install" => "/Assets/Icons/package.svg",
                "patching" => "/Assets/Icons/wrench.svg",
                "launching" => "/Assets/Icons/rocket.svg",
                "complete" => "/Assets/Icons/check.svg",
                _ => "/Assets/Icons/info.svg"
            };

            if (IsLaunchOverlayVisible) 
            {
                IsLaunchAfterDownloadVisible = msg.State == "download";

                // Title
                StatusTitle = LocalizationService.Instance.Translate($"launch.state.{msg.State}");
                if (string.IsNullOrEmpty(StatusTitle)) StatusTitle = msg.State; // Fallback
                
                // Detail
                if (msg.MessageKey.StartsWith("common.raw"))
                {
                   ProgressText = msg.Args?.FirstOrDefault()?.ToString() ?? msg.MessageKey;
                }
                else
                {
                   ProgressText = LocalizationService.Instance.Translate(msg.MessageKey, msg.Args ?? Array.Empty<object>());
                }

                DownloadProgress = msg.Progress;
                
                // Speed
                if (msg.TotalBytes > 0)
                {
                    CurrentSpeed = $"{(msg.DownloadedBytes / 1024.0 / 1024.0):N1} MB / {(msg.TotalBytes / 1024.0 / 1024.0):N1} MB";
                }
                else
                {
                    CurrentSpeed = ""; 
                }
            }
        });
    }

    private void CancelLaunch()
    {
        if (!IsLaunchOverlayVisible)
        {
            return;
        }

        _isLaunchCancelRequested = true;
        IsLaunchOverlayVisible = false;
        _gameSessionService.CancelDownload();
    }

    private void OnErrorOccurred(string type, string message, string? trace)
    {
        Dispatcher.UIThread.InvokeAsync(() => {
            ErrorMessage = message;
            ErrorTrace = trace ?? "";
            IsErrorModalOpen = true;
            IsLaunchOverlayVisible = false;
        });
    }

    private Bitmap? LoadBitmap(string uriString)
    {
        var bitmap = BitmapHelper.LoadBitmap(uriString, 1920); // Limit width to 1920px
        if (bitmap == null)
        {
            Logger.Error("Dashboard", $"Failed to load background '{uriString}'");
        }
        return bitmap;
    }

    private async Task ChangeBackgroundAsync(string uriString)
    {
        // Fade out
        BackgroundOpacity = 0;
        await Task.Delay(400); // Matches View transition duration + buffer

        // Load new
        var newBitmap = LoadBitmap(uriString);
        BackgroundImage = newBitmap;

        // Fade in
        BackgroundOpacity = 1;
    }

    private void UpdateBackground(string? mode)
    {
        // Stop any existing timer
        _backgroundTimer?.Stop();
        
        if (string.IsNullOrEmpty(mode) || mode == "auto" || mode == "slideshow") // Handle legacy slideshow value here too
        {
            // Start slideshow
            if (_slideshowImages.Count > 0)
            {
                _backgroundTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _backgroundTimer.Tick += async (s, e) => 
                {
                    _currentSlideIndex = (_currentSlideIndex + 1) % _slideshowImages.Count;
                    await ChangeBackgroundAsync(_slideshowImages[_currentSlideIndex]);
                };
                
                // Set initial random or first
                _currentSlideIndex = new Random().Next(_slideshowImages.Count);
                // Call async method without awaiting (fire-and-forget for initial set) or use simple set if we don't want fade on app start
                // But changing modes should fade.
                _ = ChangeBackgroundAsync(_slideshowImages[_currentSlideIndex]);
                
                _backgroundTimer.Start();
            }
        }
        else
        {
            // Set specific background
            var path = $"avares://HyPrism/Assets/Images/Backgrounds/{mode}";
            _ = ChangeBackgroundAsync(path);
        }
    }

    private void OpenInstancesSettings()
    {
        if (SettingsViewModel == null)
        {
            return;
        }

        SettingsViewModel.ActiveTab = "instances";
        IsSettingsOpen = true;
        SettingsViewModel.RefreshInstancesCommand.Execute().Subscribe();
    }
}
