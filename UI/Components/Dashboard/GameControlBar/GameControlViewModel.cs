using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using HyPrism.Models;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Services.Game;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace HyPrism.UI.Components.Dashboard.GameControlBar;

public class GameControlViewModel : ReactiveObject, IDisposable
{
    private readonly InstanceService _instanceService;
    private readonly FileService _fileService;
    private readonly GameProcessService _gameProcessService;
    private readonly ConfigService _configService;
    private readonly VersionService _versionService;
    private int? _cachedLatestVersion;
    private IDisposable? _gameStatusTimer;

    // Commands
    public ReactiveCommand<Unit, Unit> ToggleModsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenInstancesCommand { get; }

    // Properties
    private string _selectedBranch = "release";
    public string SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedBranch, value);
            UpdateVersionDisplay();
        }
    }
    
    private int _selectedVersion = 0;
    public int SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedVersion, value);
            UpdateVersionDisplay();
        }
    }

    private string _selectedBranchVersionLabel = "release-?";
    public string SelectedBranchVersionLabel
    {
        get => _selectedBranchVersionLabel;
        set => this.RaiseAndSetIfChanged(ref _selectedBranchVersionLabel, value);
    }


    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => this.RaiseAndSetIfChanged(ref _isGameRunning, value);
    }

    public ObservableCollection<string> Branches { get; } = new() { "release", "pre-release" };

    // Localization
    public IObservable<string> MainEducational { get; }
    public IObservable<string> MainBuyIt { get; }
    public IObservable<string> MainPlay { get; }
    public IObservable<string> BranchReleaseLabel { get; }
    public IObservable<string> BranchPreReleaseLabel { get; }

    public GameControlViewModel(
        InstanceService instanceService,
        FileService fileService,
        GameProcessService gameProcessService,
        ConfigService configService,
        VersionService versionService,
        Action<string, int> toggleMods, 
        Action toggleSettings,
        Action openInstances,
        Func<Task> launchAction,
        LocalizationService localizationService)
    {
        _instanceService = instanceService;
        _fileService = fileService;
        _gameProcessService = gameProcessService;
        _configService = configService;
        _versionService = versionService;

        var loc = localizationService;
        MainEducational = loc.GetObservable("main.educational");
        MainBuyIt = loc.GetObservable("main.buyIt");
        BranchReleaseLabel = loc.GetObservable("main.release");
        BranchPreReleaseLabel = loc.GetObservable("main.preRelease");
        
        // Dynamic Play Button Text
        MainPlay = this.WhenAnyValue(x => x.IsGameRunning)
            .CombineLatest(
                loc.GetObservable("main.play"), 
                loc.GetObservable("main.running"),
                (running, playText, runningText) => running ? (string.IsNullOrEmpty(runningText) ? "RUNNED" : runningText) : playText
            );
        
        ToggleModsCommand = ReactiveCommand.Create(() => toggleMods(SelectedBranch, SelectedVersion));
        ToggleSettingsCommand = ReactiveCommand.Create(toggleSettings);
        
        OpenFolderCommand = ReactiveCommand.Create(() =>  
        {
            var branch = SelectedBranch?.ToLower().Replace(" ", "-") ?? "release";
            // Logic moved from GameUtilityService
            string branchNormalized = UtilityService.NormalizeVersionType(branch);
            var path = _instanceService.ResolveInstancePath(branchNormalized, SelectedVersion, true);
            _fileService.OpenFolder(path);
        });

        var canLaunch = this.WhenAnyValue(x => x.IsGameRunning, running => !running);
        LaunchCommand = ReactiveCommand.CreateFromTask(launchAction, canLaunch);

        OpenInstancesCommand = ReactiveCommand.Create(openInstances);

        SelectedBranch = UtilityService.NormalizeVersionType(_configService.Configuration.VersionType);
        SelectedVersion = _configService.Configuration.SelectedVersion;
        LoadCachedLatestVersion();
        UpdateVersionDisplay();

        // Periodically check for game process status (store disposable to stop on Dispose)
        _gameStatusTimer = DispatcherTimer.Run(() => 
        {
            IsGameRunning = _gameProcessService.CheckForRunningGame();
            return true;
        }, TimeSpan.FromSeconds(2));
        
        // Subscribe to launch version changes from Settings
        _versionChangeSubscription = MessageBus.Current.Listen<LaunchVersionChangedMessage>()
            .Subscribe(msg =>
            {
                SelectedBranch = msg.Branch;
                SelectedVersion = msg.Version;
                LoadCachedLatestVersion();
                UpdateVersionDisplay();
            });
    }

    private IDisposable? _versionChangeSubscription;

    public void Dispose()
    {
        _gameStatusTimer?.Dispose();
        _gameStatusTimer = null;
        _versionChangeSubscription?.Dispose();
        _versionChangeSubscription = null;
    }

    private void LoadCachedLatestVersion()
    {
        _cachedLatestVersion = null;
        if (_versionService.TryGetCachedVersions(SelectedBranch, TimeSpan.FromMinutes(15), out var cached)
            && cached.Count > 0)
        {
            _cachedLatestVersion = cached[0];
        }
    }

    private void UpdateVersionDisplay(int? latestVersionOverride = null)
    {
        var branchLabel = SelectedBranch == "pre-release" ? "prerelease" : "release";
        var version = SelectedVersion;
        if (version == 0)
        {
            version = latestVersionOverride ?? _cachedLatestVersion ?? 0;
        }

        SelectedBranchVersionLabel = version > 0 ? $"{branchLabel}-{version}" : $"{branchLabel}-latest";
    }
}
