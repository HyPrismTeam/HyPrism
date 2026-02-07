using ReactiveUI;
using System.Reactive;
using HyPrism.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using System.Linq;
using HyPrism.Services.Core;
using HyPrism.Services.Game;
using HyPrism.Models;
using System;
using System.Reactive.Linq;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Collections.ObjectModel;
using Avalonia.Threading;

using System.Threading;
using System.Windows.Input;
using HyPrism.UI.Helpers;
using System.Linq;
using System.Reactive.Disposables;
using HyPrism.Services.User;
using HyPrism.UI.Views.ProfileEditorView;

namespace HyPrism.UI.Views.SettingsView;

public class BranchItem
{
    public string DisplayName { get; set; } = "";
    public string Value { get; set; } = "";
    public string IconPath { get; set; } = "";
}

public class LanguageItem
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FlagIconPath { get; set; } = "";
}

public class BackgroundItem : ReactiveObject
{
    public string Filename { get; set; } = "";
    public string FullPath { get; set; } = "";
    public Bitmap? Thumbnail { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public class AccentColorItem : ReactiveObject
{
    public Color Color { get; set; }
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public class CreditProfile : ReactiveObject
{
    public string Name { get; set; } = "";
    private string _role = "";
    public string Role 
    {
        get => _role;
        set => this.RaiseAndSetIfChanged(ref _role, value);
    }
    public string RoleType { get; set; } = "contributor"; // maintainer, auth, contributor
    
    public string ProfileUrl { get; set; } = "";
    public string AvatarUrl { get; set; } = "";

    private Bitmap? _avatar;
    public Bitmap? Avatar
    {
        get => _avatar;
        set => this.RaiseAndSetIfChanged(ref _avatar, value);
    }

    public bool IsOverflow { get; set; }
    public int OverflowCount { get; set; }

    public ICommand? OpenCommand { get; set; }
}

public class ProfileItemViewModel : ReactiveObject
{
    private readonly Profile _profile;
    private readonly SettingsViewModel _parent;
    private readonly ILocalizationService _localizationService;
    private readonly ObservableAsPropertyHelper<string> _createdAtString;
    
    public Profile Profile => _profile;
    public string Name => _profile.Name;
    public string UUID => _profile.UUID;
    public string ShortUUID => _profile.UUID.Length > 13 ? _profile.UUID.Substring(0, 13) + "..." : _profile.UUID;
    
    public bool IsOfficial => _profile.IsOfficial;
    
    // Formatting for UI
    public string PlaytimeString 
    {
        get 
        {
            var hours = (int)_profile.TotalPlaytime.TotalHours;
            if (hours == 0) return $"{_profile.TotalPlaytime.Minutes}m played";
            return $"{hours}h {_profile.TotalPlaytime.Minutes}m played";
        }
    }
    
    public string CreatedAtString => _createdAtString.Value;
    
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }
    
    public ICommand ActivateCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    
    public ProfileItemViewModel(Profile profile, bool isActive, SettingsViewModel parent, ILocalizationService localizationService)
    {
        _profile = profile;
        _parent = parent;
        _isActive = isActive;
        _localizationService = localizationService;
        
        _createdAtString = _localizationService.WhenAnyValue(x => x.CurrentLanguage)
            .StartWith(_localizationService.CurrentLanguage)
            .Select(lang => 
            {
                try 
                {
                    var culture = new System.Globalization.CultureInfo(lang);
                    return $"Created {_profile.CreatedAt.ToString("MMM d, yyyy", culture)}";
                }
                catch
                {
                    return $"Created {_profile.CreatedAt:MMM d, yyyy}";
                }
            })
            .ToProperty(this, x => x.CreatedAtString);
        
        ActivateCommand = ReactiveCommand.Create(() => parent.ActivateProfile(_profile));
        EditCommand = ReactiveCommand.Create(() => parent.EditProfile(_profile));
        DeleteCommand = ReactiveCommand.Create(() => parent.DeleteProfile(_profile));
    }
}

public class SettingsViewModel : ReactiveObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly ConfigService _configService;
    private readonly FileDialogService _fileDialogService;
    private readonly InstanceService _instanceService;
    private readonly FileService _fileService;
    private readonly GitHubService _gitHubService;
    private readonly BrowserService _browserService;
    private readonly VersionService _versionService;
    private readonly IClipboardService _clipboardService;
    private readonly IProfileManagementService _profileManagementService;
    private readonly ILocalizationService _localizationService;
    private bool _disposed;
    private readonly CompositeDisposable _subscriptions = new();

    private string _branchIconAccentCss = "* { stroke: #FFA845; fill: none; }";
    public string BranchIconAccentCss
    {
        get => _branchIconAccentCss;
        set => this.RaiseAndSetIfChanged(ref _branchIconAccentCss, value);
    }
    
    public LocalizationService Localization { get; }


    // Reactive Localization Properties - will update automatically when language changes
    public IObservable<string> SettingsTitle { get; }
    public IObservable<string> MyProfile { get; }
    public IObservable<string> General { get; }
    public IObservable<string> Visuals { get; }
    public IObservable<string> Language { get; }
    public IObservable<string> Data { get; }
    public IObservable<string> InstancesTabTitle { get; }
    public IObservable<string> About { get; }
    
    // Tab Contents
    // Profile
    public IObservable<string> ProfileHeader { get; }
    public IObservable<string> ProfileAvatar { get; }
    public IObservable<string> ProfileUploadSkin { get; }
    public IObservable<string> ProfileDisplayName { get; }
    public IObservable<string> ProfileDisplayNameHint { get; }
    public IObservable<string> ProfileNameWarning { get; }
    public IObservable<string> ProfileUuid { get; }
    public IObservable<string> ProfileUuidWarning { get; }

    // General
    public IObservable<string> GeneralHeader { get; }
    public IObservable<string> GeneralLauncherStorage { get; }
    public IObservable<string> GeneralBrowse { get; }
    public IObservable<string> GeneralLauncherStorageHint { get; }
    public IObservable<string> GeneralUpdateChannel { get; }
    public IObservable<string> GeneralUpdateChannelHint { get; }
    public IObservable<string> GeneralCloseLauncher { get; }
    public IObservable<string> GeneralCloseLauncherHint { get; }
    public IObservable<string> GeneralDisableNews { get; }
    public IObservable<string> GeneralDisableNewsHint { get; }
    
    // Visual
    public IObservable<string> VisualHeader { get; }
    public IObservable<string> VisualAccentColor { get; }
    public IObservable<string> VisualBackground { get; }
    public IObservable<string> VisualAutoShuffle { get; }
    public IObservable<string> VisualCurrent { get; }
    
    // Language
    public IObservable<string> LanguageHeader { get; }
    public IObservable<string> LanguageInterface { get; }
    public IObservable<string> LanguageInterfaceHint { get; }
    public IObservable<string> LanguageNote { get; }

    // Data
    public IObservable<string> DataHeader { get; }
    public IObservable<string> DataGameDir { get; }
    public IObservable<string> DataGameDirHint { get; }
    public IObservable<string> DataLauncherDir { get; }
    public IObservable<string> DataLauncherDirHint { get; }
    public IObservable<string> DataClean { get; }
    public IObservable<string> DataCleanHint { get; }
    public IObservable<string> DataOpen { get; }
    public IObservable<string> DataCleanAction { get; }

    // Instances
    public IObservable<string> InstancesHeader { get; }
    public IObservable<string> InstancesDesc { get; }
    public IObservable<string> InstancesOpenFolder { get; }
    public IObservable<string> InstancesDelete { get; }
    public IObservable<string> InstancesLaunchBranch { get; }
    public IObservable<string> InstancesEmpty { get; }
    public IObservable<string> InstancesUserData { get; }
    public IObservable<string> InstancesLatest { get; }
    
    // About
    public IObservable<string> AboutTitle { get; }
    public IObservable<string> AboutDisclaimer { get; }
    public IObservable<string> AboutDescription { get; }
    public IObservable<string> AboutContributorsDescription { get; }

    // Credits
    public ObservableCollection<CreditProfile> Maintainers { get; } = new();
    public ObservableCollection<CreditProfile> Contributors { get; } = new();

    // Tabs
    private string _activeTab = "profile";
    public string ActiveTab
    {
        get => _activeTab;
        set => this.RaiseAndSetIfChanged(ref _activeTab, value);
    }

    // Multi-Profile Logic
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();
    public ObservableCollection<ProfileItemViewModel> OtherProfiles { get; } = new();

    private ProfileItemViewModel? _activeProfileItem;
    public ProfileItemViewModel? ActiveProfileItem
    {
        get => _activeProfileItem;
        set => this.RaiseAndSetIfChanged(ref _activeProfileItem, value);
    }

    private bool _isProfileEditorOpen;
    public bool IsProfileEditorOpen
    {
        get => _isProfileEditorOpen;
        set => this.RaiseAndSetIfChanged(ref _isProfileEditorOpen, value);
    }

    private ProfileEditorViewModel? _currentProfileEditor;
    public ProfileEditorViewModel? CurrentProfileEditor
    {
        get => _currentProfileEditor;
        set => this.RaiseAndSetIfChanged(ref _currentProfileEditor, value);
    }

    public ICommand CreateProfileCommand => ReactiveCommand.Create(() => OpenProfileEditor(null));

    public void LoadProfiles()
    {
        Profiles.Clear();
        OtherProfiles.Clear();
        ActiveProfileItem = null;

        var profiles = _profileManagementService.GetProfiles();
        var activeIndex = _profileManagementService.GetActiveProfileIndex();

        for (int i = 0; i < profiles.Count; i++)
        {
            var vm = new ProfileItemViewModel(profiles[i], i == activeIndex, this, _localizationService);
            if (i == activeIndex)
            {
                ActiveProfileItem = vm;
            }
            else
            {
                OtherProfiles.Add(vm);
            }
            Profiles.Add(vm);
        }
    }

    public void ActivateProfile(Profile profile)
    {
        var profiles = _profileManagementService.GetProfiles();
        var index = profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _profileManagementService.SwitchProfile(index);
            LoadProfiles();
            this.RaisePropertyChanged(nameof(Nick));
            this.RaisePropertyChanged(nameof(UUID));
        }
    }

    public void EditProfile(Profile profile) => OpenProfileEditor(profile);
    
    public void DeleteProfile(Profile profile)
    {
        if (Profiles.Count <= 1) return;
        _profileManagementService.DeleteProfile(profile.Id);
        LoadProfiles();
    }
    
    private void OpenProfileEditor(Profile? profile)
    {
        CurrentProfileEditor = new ProfileEditorViewModel(_profileManagementService, _fileService, _clipboardService);
        CurrentProfileEditor.Initialize(profile);
        CurrentProfileEditor.OnRequestClose += () => IsProfileEditorOpen = false;
        CurrentProfileEditor.OnSaved += LoadProfiles;
        IsProfileEditorOpen = true;
    }


    // Profile
    private string _nick;
    public string Nick
    {
        get => _nick;
        set
        {
            _configService.Configuration.Nick = value;
            _configService.SaveConfig();
            this.RaiseAndSetIfChanged(ref _nick, value);
        }
    }

    private string _uuid;
    public string UUID
    {
        get => _uuid;
        set
        {
            _configService.Configuration.UUID = value;
            _configService.SaveConfig();
            this.RaiseAndSetIfChanged(ref _uuid, value);
        }
    }

    // General
    public bool CloseAfterLaunch
    {
        get => _settingsService.GetCloseAfterLaunch();
        set
        {
            if (_settingsService.GetCloseAfterLaunch() != value)
            {
                _settingsService.SetCloseAfterLaunch(value);
                this.RaisePropertyChanged();
            }
        }
    }

    public bool DisableNews
    {
        get => _settingsService.GetDisableNews();
        set
        {
            if (_settingsService.GetDisableNews() != value)
            {
                _settingsService.SetDisableNews(value);
                this.RaisePropertyChanged();
            }
        }
    }

    private string _launcherDataDirectory;
    public string LauncherDataDirectory
    {
        get => _launcherDataDirectory;
        set => this.RaiseAndSetIfChanged(ref _launcherDataDirectory, value);
    }

    public string InstanceDirectory => _instanceService.GetInstanceRoot();
    
    private List<BranchItem> _branchItems = new();
    public List<BranchItem> BranchItems
    {
        get => _branchItems;
        set => this.RaiseAndSetIfChanged(ref _branchItems, value);
    }

    private List<BranchItem> _launchBranchItems = new();
    public List<BranchItem> LaunchBranchItems
    {
        get => _launchBranchItems;
        set => this.RaiseAndSetIfChanged(ref _launchBranchItems, value);
    }

    private List<int> _launchVersions = new();
    public List<int> LaunchVersions
    {
        get => _launchVersions;
        set => this.RaiseAndSetIfChanged(ref _launchVersions, value);
    }
    
    private BranchItem? _selectedBranchItem;
    public BranchItem? SelectedBranchItem
    {
        get => _selectedBranchItem;
        set
        {
            var old = _selectedBranchItem;
            this.RaiseAndSetIfChanged(ref _selectedBranchItem, value);
            
            // Only update service if value actually changed (ignores object reference changes due to translation updates)
            if (value != null && (old == null || old.Value != value.Value))
            {
                _settingsService.SetLauncherBranch(value.Value);
            }
        }
    }

    private bool _isInitializingBranches;
    private bool _suppressVersionSave;
    private CancellationTokenSource? _versionRefreshCts;
    private BranchItem? _selectedLaunchBranchItem;
    public BranchItem? SelectedLaunchBranchItem
    {
        get => _selectedLaunchBranchItem;
        set
        {
            var old = _selectedLaunchBranchItem;
            this.RaiseAndSetIfChanged(ref _selectedLaunchBranchItem, value);
            this.RaisePropertyChanged(nameof(SelectedBranchIconPath));
            this.RaisePropertyChanged(nameof(IsReleaseBranchSelected));
            this.RaisePropertyChanged(nameof(IsPreReleaseBranchSelected));

            if (!_isInitializingBranches && value != null && (old == null || old.Value != value.Value))
            {
                _suppressVersionSave = true;
                SelectedLaunchVersion = null;
                _suppressVersionSave = false;
                _ = RefreshLaunchVersionsAsync();
            }
        }
    }

    private BranchItem? _appliedLaunchBranchItem;
    public BranchItem? AppliedLaunchBranchItem
    {
        get => _appliedLaunchBranchItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _appliedLaunchBranchItem, value);
            this.RaisePropertyChanged(nameof(SelectedBranchIconPath));
        }
    }

    // Branch selection helpers for ToggleButtons
    public bool IsReleaseBranchSelected => SelectedLaunchBranchItem?.Value == "release";
    public bool IsPreReleaseBranchSelected => SelectedLaunchBranchItem?.Value == "pre-release";
    
    public IObservable<string> BranchReleaseLabel { get; private set; } = null!;
    public IObservable<string> BranchPreReleaseLabel { get; private set; } = null!;
    
    public ReactiveCommand<Unit, Unit> SelectReleaseBranchCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SelectPreReleaseBranchCommand { get; private set; } = null!;

    public string SelectedBranchIconPath =>
        AppliedLaunchBranchItem?.Value == "pre-release"
            ? "/Assets/Icons/git-pull-request-draft.svg"
            : "/Assets/Icons/git-commit-vertical.svg";

    private int? _appliedLaunchVersion;
    public int? AppliedLaunchVersion
    {
        get => _appliedLaunchVersion;
        set => this.RaiseAndSetIfChanged(ref _appliedLaunchVersion, value);
    }

    private int? _selectedLaunchVersion;
    public int? SelectedLaunchVersion
    {
        get => _selectedLaunchVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLaunchVersion, value);
            if (_suppressVersionSave)
            {
                return;
            }
            _configService.Configuration.SelectedVersion = value ?? 0;
            if (value != null && SelectedLaunchBranchItem != null)
            {
                _configService.Configuration.VersionType = UtilityService.NormalizeVersionType(SelectedLaunchBranchItem.Value);
                AppliedLaunchBranchItem = SelectedLaunchBranchItem;
                AppliedLaunchVersion = value;
            }
            else if (value == null && SelectedLaunchBranchItem != null)
            {
                AppliedLaunchBranchItem = SelectedLaunchBranchItem;
                AppliedLaunchVersion = 0;
            }
            _configService.SaveConfig();
            
            // Notify GameControlViewModel about the change
            var branch = SelectedLaunchBranchItem?.Value ?? "release";
            MessageBus.Current.SendMessage(new LaunchVersionChangedMessage(branch, value ?? 0));
        }
    }
    
    // Language
    public List<LanguageItem> LanguageItems { get; }
    
    // Visuals
    public List<AccentColorItem> AccentColors { get; } =
    [
        new() { Color = Color.Parse("#FFA845") }, // Orange
        new() { Color = Color.Parse("#3B82F6") }, // Blue
        new() { Color = Color.Parse("#10B981") }, // Emerald
        new() { Color = Color.Parse("#8B5CF6") }, // Violet
        new() { Color = Color.Parse("#EC4899") }, // Pink
        new() { Color = Color.Parse("#F59E0B") }, // Amber
        new() { Color = Color.Parse("#EF4444") }, // Red
        new() { Color = Color.Parse("#06B6D4") }, // Cyan
        new() { Color = Color.Parse("#A855F7") }, // Purple
        new() { Color = Color.Parse("#6366F1") }, // Indigo
        new() { Color = Color.Parse("#14B8A6") }, // Teal
        new() { Color = Color.Parse("#F43F5E") }  // Rose
    ];

    public List<BackgroundItem> Backgrounds { get; }

    // Instances
    private List<InstalledInstance> _instances = new();
    public List<InstalledInstance> Instances
    {
        get => _instances;
        set => this.RaiseAndSetIfChanged(ref _instances, value);
    }
    
    // Commands
    public ReactiveCommand<AccentColorItem, Unit> SetAccentColorCommand { get; }
    public ReactiveCommand<string, Unit> SetBackgroundCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInstancesCommand { get; }
    public ReactiveCommand<InstalledInstance, Unit> OpenInstanceFolderCommand { get; }
    public ReactiveCommand<InstalledInstance, Unit> DeleteInstanceCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLauncherFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenInstancesFolderCommand { get; }
    
    private LanguageItem? _selectedLanguageItem;
    public LanguageItem? SelectedLanguageItem
    {
        get => _selectedLanguageItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguageItem, value);
            if (value != null)
            {
                _settingsService.SetLanguage(value.Code);
            }
        }
    }
    
    // Commands
    public ReactiveCommand<string, Unit> SwitchTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; } // Handled by View
    public ReactiveCommand<Unit, Unit> BrowseLauncherDataCommand { get; }
    public ReactiveCommand<Unit, Unit> RandomizeUuidCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyUuidCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGithubCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDiscordCommand { get; }
    public ReactiveCommand<Unit, Unit> ReportBugCommand { get; }

    private readonly AppPathConfiguration _appPathConfiguration;

    public SettingsViewModel(
        SettingsService settingsService,
        ConfigService configService,
        FileDialogService fileDialogService,
        LocalizationService localizationService,
        InstanceService instanceService,
        FileService fileService,
        GitHubService gitHubService,
        BrowserService browserService,
        AppPathConfiguration appPathConfiguration,
        VersionService versionService,
        IClipboardService clipboardService,
        IProfileManagementService profileManagementService)
    {
        _settingsService = settingsService;
        _configService = configService;
        _fileDialogService = fileDialogService;
        _localizationService = localizationService;
        _instanceService = instanceService;
        _fileService = fileService;
        _gitHubService = gitHubService;
        _browserService = browserService;
        Localization = localizationService;
        _appPathConfiguration = appPathConfiguration;
        _versionService = versionService;
        _clipboardService = clipboardService;
        _profileManagementService = profileManagementService;

        BranchIconAccentCss = BuildBranchIconCss(_settingsService.GetAccentColor());
        LoadProfiles();
        
        // Initialize reactive localization properties - these will update automatically
        var loc = Localization;
        SettingsTitle = loc.GetObservable("settings.title");
        MyProfile = loc.GetObservable("settings.myProfile");
        General = loc.GetObservable("settings.general");
        Visuals = loc.GetObservable("settings.visuals");
        Language = loc.GetObservable("settings.language");
        Data = loc.GetObservable("settings.data");
        InstancesTabTitle = loc.GetObservable("settings.instances");
        About = loc.GetObservable("settings.about");

        // Profile
        ProfileHeader = loc.GetObservable("settings.profile.title");
        ProfileAvatar = loc.GetObservable("settings.profile.avatar");
        ProfileUploadSkin = loc.GetObservable("settings.profile.uploadSkin");
        ProfileDisplayName = loc.GetObservable("settings.profile.displayName");
        ProfileDisplayNameHint = loc.GetObservable("settings.profile.displayNameHint");
        ProfileNameWarning = loc.GetObservable("settings.profile.nameWarning");
        ProfileUuid = loc.GetObservable("settings.profile.uuid");
        ProfileUuidWarning = loc.GetObservable("settings.profile.uuidWarning");

        // General
        GeneralHeader = loc.GetObservable("settings.generalSettings.title");
        GeneralLauncherStorage = loc.GetObservable("settings.generalSettings.launcherStorage");
        GeneralBrowse = loc.GetObservable("settings.browse");
        GeneralLauncherStorageHint = loc.GetObservable("settings.generalSettings.launcherStorageHint");
        GeneralUpdateChannel = loc.GetObservable("settings.generalSettings.updateChannel");
        GeneralUpdateChannelHint = loc.GetObservable("settings.generalSettings.updateChannelHint");
        GeneralCloseLauncher = loc.GetObservable("settings.generalSettings.closeLauncher");
        GeneralCloseLauncherHint = loc.GetObservable("settings.generalSettings.closeLauncherHint");
        GeneralDisableNews = loc.GetObservable("settings.generalSettings.disableNews");
        GeneralDisableNewsHint = loc.GetObservable("settings.generalSettings.disableNewsHint");
        
        // Visual
        VisualHeader = loc.GetObservable("settings.visualSettings.title");
        VisualAccentColor = loc.GetObservable("settings.visualSettings.accentColor");
        VisualBackground = loc.GetObservable("settings.visualSettings.background");
        VisualAutoShuffle = loc.GetObservable("settings.visualSettings.autoShuffle");
        VisualCurrent = loc.GetObservable("settings.visualSettings.current");
        
        // Language
        LanguageHeader = loc.GetObservable("settings.languageSettings.title");
        LanguageInterface = loc.GetObservable("settings.languageSettings.interfaceLanguage");
        LanguageInterfaceHint = loc.GetObservable("settings.languageSettings.interfaceLanguageHint");
        LanguageNote = loc.GetObservable("settings.languageSettings.note");

        // Data
        DataHeader = loc.GetObservable("settings.dataSettings.title");
        DataGameDir = loc.GetObservable("settings.dataSettings.gameDirectory");
        DataGameDirHint = loc.GetObservable("settings.dataSettings.gameDirectoryHint");
        DataLauncherDir = loc.GetObservable("settings.dataSettings.launcherData");
        DataLauncherDirHint = loc.GetObservable("settings.dataSettings.launcherDataHint");
        DataClean = loc.GetObservable("settings.dataSettings.cleanData");
        DataCleanHint = loc.GetObservable("settings.dataSettings.cleanDataHint");
        DataOpen = loc.GetObservable("settings.dataSettings.open");
        DataCleanAction = loc.GetObservable("settings.dataSettings.cleanDataAction");

        // Instances
        InstancesHeader = loc.GetObservable("settings.instanceSettings.title");
        InstancesDesc = loc.GetObservable("settings.instanceSettings.description");
        InstancesOpenFolder = loc.GetObservable("settings.instanceSettings.openFolder");
        InstancesDelete = loc.GetObservable("settings.instanceSettings.delete");
        InstancesLaunchBranch = loc.GetObservable("settings.instanceSettings.launchBranch");
        InstancesEmpty = loc.GetObservable("settings.instanceSettings.empty");
        InstancesUserData = loc.GetObservable("settings.instanceSettings.userData");
        InstancesLatest = loc.GetObservable("main.latest");
        
        // About
        AboutTitle = loc.GetObservable("settings.aboutSettings.title");
        AboutDisclaimer = loc.GetObservable("settings.aboutSettings.disclaimer");
        AboutDescription = loc.GetObservable("settings.aboutSettings.description");
        AboutContributorsDescription = loc.GetObservable("settings.aboutSettings.contributorsDescription");

        // Dynamic Role Localization
        Observable.CombineLatest(
            loc.GetObservable("settings.aboutSettings.maintainerRole"),
            loc.GetObservable("settings.aboutSettings.authRole"),
            loc.GetObservable("settings.aboutSettings.contributorRole"),
            loc.GetObservable("settings.aboutSettings.others"),
            (m, a, c, o) => (m, a, c, o)
        ).Subscribe(t => UpdateCreditRoles(t.m, t.a, t.c, t.o))
        .DisposeWith(_subscriptions);

        _ = InitializeCreditsAsync();

        // Update branch items when language changes
        // Initialize branch labels for ToggleButtons
        BranchReleaseLabel = loc.GetObservable("main.release");
        BranchPreReleaseLabel = loc.GetObservable("main.preRelease");
        
        // Initialize branch selection commands
        SelectReleaseBranchCommand = ReactiveCommand.Create(() =>
        {
            var releaseItem = LaunchBranchItems.FirstOrDefault(x => x.Value == "release");
            if (releaseItem != null)
            {
                SelectedLaunchBranchItem = releaseItem;
            }
        });
        
        SelectPreReleaseBranchCommand = ReactiveCommand.Create(() =>
        {
            var preReleaseItem = LaunchBranchItems.FirstOrDefault(x => x.Value == "pre-release");
            if (preReleaseItem != null)
            {
                SelectedLaunchBranchItem = preReleaseItem;
            }
        });

        Observable.CombineLatest(
            loc.GetObservable("settings.generalSettings.updateChannelStable"),
            loc.GetObservable("settings.generalSettings.updateChannelBeta"),
            (stable, beta) => new List<BranchItem>
            {
                new BranchItem { DisplayName = stable, Value = "release" },
                new BranchItem { DisplayName = beta, Value = "beta" }
            })
            .Subscribe(items =>
            {
                BranchItems = items;
                // Restore selection
                var current = _settingsService.GetLauncherBranch();
                SelectedBranchItem = items.FirstOrDefault(x => x.Value == current) ?? items.FirstOrDefault();
            })
            .DisposeWith(_subscriptions);

        Observable.CombineLatest(
            loc.GetObservable("main.release"),
            loc.GetObservable("main.preRelease"),
            (release, preRelease) => new List<BranchItem>
            {
                new BranchItem { DisplayName = release, Value = "release", IconPath = "/Assets/Icons/git-commit-vertical.svg" },
                new BranchItem { DisplayName = preRelease, Value = "pre-release", IconPath = "/Assets/Icons/git-pull-request-draft.svg" }
            })
            .Subscribe(items =>
            {
                _isInitializingBranches = true;
                LaunchBranchItems = items;
                var current = UtilityService.NormalizeVersionType(_configService.Configuration.VersionType);
                AppliedLaunchBranchItem = items.FirstOrDefault(x => x.Value == current) ?? items.FirstOrDefault();
                SelectedLaunchBranchItem = items.FirstOrDefault(x => x.Value == current) ?? items.FirstOrDefault();
                _isInitializingBranches = false;
            })
            .DisposeWith(_subscriptions);
        
        // Initialize language items - load names from locale files
        LanguageItems = LocalizationService.GetAvailableLanguages()
            .Select(kvp => 
            {
                // Derive flag code from locale code (e.g. en-US -> us, ru-RU -> ru)
                var countryCode = kvp.Key.Contains('-') ? kvp.Key.Split('-')[1].ToLower() : kvp.Key.ToLower();
                
                // Edge case handling if needed (e.g. specific overrides), but standard ISO usually works
                // ja-JP -> jp, ko-KR -> kr, zh-CN -> cn
                
                return new LanguageItem 
                { 
                    Code = kvp.Key, 
                    DisplayName = kvp.Value,
                    FlagIconPath = $"/Assets/Icons/Flags/{countryCode}.svg"
                };
            })
            .OrderBy(l => l.DisplayName)
            .ToList();
        
        // Initialize properties
        _nick = _configService.Configuration.Nick;
        _uuid = _configService.Configuration.UUID ?? "";
        
        var configuredDataDir = _settingsService.GetLauncherDataDirectory();
        _launcherDataDirectory = string.IsNullOrEmpty(configuredDataDir) ? _appPathConfiguration.AppDir : configuredDataDir;
        
        // Initialize branch selection
        var currentBranch = _settingsService.GetLauncherBranch();
        _selectedBranchItem = BranchItems.FirstOrDefault(b => b.Value == currentBranch) ?? BranchItems[0];

        var currentLaunchBranch = UtilityService.NormalizeVersionType(_configService.Configuration.VersionType);
        _selectedLaunchBranchItem = LaunchBranchItems.FirstOrDefault(b => b.Value == currentLaunchBranch) ?? LaunchBranchItems.FirstOrDefault();
        _appliedLaunchBranchItem = _selectedLaunchBranchItem;
        _selectedLaunchVersion = _configService.Configuration.SelectedVersion;
        _appliedLaunchVersion = _selectedLaunchVersion;
        
        // Initialize language selection
        var currentLanguage = _configService.Configuration.Language;
        _selectedLanguageItem = LanguageItems.FirstOrDefault(l => l.Code == currentLanguage) ?? LanguageItems.First(l => l.Code == "en-US");
        
        SwitchTabCommand = ReactiveCommand.Create<string>(tab => ActiveTab = tab);
        CloseCommand = ReactiveCommand.Create(() => { });
        BrowseLauncherDataCommand = ReactiveCommand.CreateFromTask(BrowseLauncherDataAsync);
        RandomizeUuidCommand = ReactiveCommand.Create(RandomizeUuid);
        CopyUuidCommand = ReactiveCommand.CreateFromTask(CopyUuidAsync);
        
        OpenGithubCommand = ReactiveCommand.Create(() => { browserService.OpenURL("https://github.com/yyyumeniku/HyPrism"); });
        OpenDiscordCommand = ReactiveCommand.Create(() => { browserService.OpenURL("https://discord.com/invite/ekZqTtynjp"); });
        ReportBugCommand = ReactiveCommand.Create(() => { browserService.OpenURL("https://github.com/yyyumeniku/HyPrism/issues/new"); });
        
        // New Commands
        SetAccentColorCommand = ReactiveCommand.Create<AccentColorItem>(SetAccentColor);
        SetBackgroundCommand = ReactiveCommand.Create<string>(SetBackground);
        RefreshInstancesCommand = ReactiveCommand.Create(RefreshInstances);
        OpenInstanceFolderCommand = ReactiveCommand.Create<InstalledInstance>(OpenInstanceFolder);
        DeleteInstanceCommand = ReactiveCommand.Create<InstalledInstance>(DeleteInstance);
        
        OpenLauncherFolderCommand = ReactiveCommand.Create(() => 
        {
            var path = _settingsService.GetLauncherDataDirectory();
            _fileService.OpenFolder(path);
        });
        
        OpenInstancesFolderCommand = ReactiveCommand.Create(() => 
        {
            var path = _instanceService.GetInstanceRoot();
            _fileService.OpenFolder(path);
        });
        
        Backgrounds = _settingsService.GetAvailableBackgrounds()
            .Select(x => 
            {
                var fullPath = $"avares://HyPrism/Assets/Images/Backgrounds/{x}";
                Bitmap? thumbnail = null;
                try
                {
                    // Efficiently load thumbnail (decode width 240)
                    thumbnail = BitmapHelper.LoadBitmap(fullPath, 240);
                }
                catch (Exception ex)
                {
                    Logger.Error("Settings", $"Failed to load background asset '{fullPath}': {ex.Message}");
                }

                return new BackgroundItem 
                { 
                    Filename = x,
                    FullPath = fullPath,
                    Thumbnail = thumbnail
                };
            })
            .ToList();
            
        // Debug: Log found backgrounds
        Logger.Info("Settings", $"Found {Backgrounds.Count} backgrounds");
        
        // Listen to background changes to update selection state
        _settingsService.OnBackgroundChanged += OnBackgroundChanged;
        
        // Listen to accent color changes
        _settingsService.OnAccentColorChanged += OnAccentColorChanged;
        
        // Initial selection state
        UpdateBackgroundSelection(_settingsService.GetBackgroundMode());
        UpdateAccentColorSelection(_settingsService.GetAccentColor());

        // Initial load
        RefreshInstances();
        _ = RefreshLaunchVersionsAsync();
    }

    private async Task RefreshLaunchVersionsAsync()
    {
        _versionRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _versionRefreshCts = cts;

        try
        {
            var branch = UtilityService.NormalizeVersionType(SelectedLaunchBranchItem?.Value ?? _configService.Configuration.VersionType);

            // 1. Check fresh cache (< 15 min old) — use immediately, no fetch
            bool hasFreshCache = _versionService.TryGetCachedVersions(branch, TimeSpan.FromMinutes(15), out var freshCached) && freshCached.Count > 0;

            if (hasFreshCache)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    ApplyVersionList(freshCached, cts.Token);
                }
                return;
            }

            // 2. Check stale cache — display immediately, then fetch in background (don't update UI)
            bool hasStaleCache = _versionService.TryGetCachedVersions(branch, TimeSpan.FromDays(3650), out var staleCached) && staleCached.Count > 0;

            if (hasStaleCache && !cts.Token.IsCancellationRequested)
            {
                ApplyVersionList(staleCached, cts.Token);
            }

            // Fetch in background to refresh cache for next time
            if (!cts.Token.IsCancellationRequested)
            {
                var fetched = await _versionService.GetVersionListAsync(branch, cts.Token);

                // Only update UI if there was no stale cache shown (first-ever load for this branch)
                if (!hasStaleCache && !cts.Token.IsCancellationRequested)
                {
                    ApplyVersionList(fetched, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a newer request — ignore
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsViewModel", $"Failed to load launch versions: {ex.Message}");
        }
    }

    private void ApplyVersionList(List<int> versions, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var displayVersions = versions.OrderByDescending(x => x).ToList();
        displayVersions.Insert(0, 0); // 0 = "latest"

        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested) return;

            _suppressVersionSave = true;
            LaunchVersions = displayVersions;

            var pending = SelectedLaunchBranchItem?.Value;
            var applied = AppliedLaunchBranchItem?.Value;

            if (!string.IsNullOrEmpty(pending) && pending != applied)
            {
                // Different branch selected - don't highlight any version until user picks one
                SelectedLaunchVersion = null;
            }
            else
            {
                // Same branch - restore the applied version
                var configured = _configService.Configuration.SelectedVersion;
                if (configured > 0 && displayVersions.Contains(configured))
                {
                    SelectedLaunchVersion = configured;
                }
                else
                {
                    // Only show "latest" as selected if it's actually applied for this branch
                    SelectedLaunchVersion = (AppliedLaunchVersion == 0 || AppliedLaunchVersion == null) ? 0 : (int?)null;
                }
            }
            _suppressVersionSave = false;
        });
    }

    private void OnBackgroundChanged(string? mode)
    {
        UpdateBackgroundSelection(mode);
    }
    
    private void OnAccentColorChanged(string color)
    {
        UpdateAccentColorSelection(color);
        BranchIconAccentCss = BuildBranchIconCss(color);
    }

    private static string BuildBranchIconCss(string color)
    {
        return $"* {{ stroke: {color} !important; fill: none !important; }}";
    }

    private void UpdateBackgroundSelection(string? mode)
    {
        if (Backgrounds == null) return;
        
        // If mode is null/empty, it usually means default or auto, but let's check exact string
        // The buttons pass "auto" or the filename
        
        var currentMode = string.IsNullOrEmpty(mode) ? "bg_1.jpg" : mode;

        foreach (var bg in Backgrounds)
        {
            if (currentMode == "auto")
            {
                 // If "auto" is selected, we deselect all specific images? 
                 // Or we need an "Auto" item?
                 // The UI has a separate button for "Auto / Shuffle".
                 // So if mode is "auto", all images should be deselected.
                 bg.IsSelected = false;
            }
            else
            {
                bg.IsSelected = bg.Filename == currentMode;
            }
        }
    }

    private void RefreshInstances()
    {
        Instances = _instanceService.GetInstalledInstances();
    }

    private void UpdateAccentColorSelection(string hexColor)
    {
        if (AccentColors == null) return;
        
        if (Color.TryParse(hexColor, out Color currentColor))
        {
             foreach (var item in AccentColors)
             {
                 item.IsSelected = item.Color == currentColor;
             }
        }
    }

    private void SetAccentColor(AccentColorItem item)
    {
        _settingsService.SetAccentColor(item.Color.ToString());
    }
    
    private string _txtMaintainer = "", _txtAuth = "", _txtContributor = "", _txtOthers = "";

    private void UpdateCreditRoles(string m, string a, string c, string o)
    {
        _txtMaintainer = m;
        _txtAuth = a;
        _txtContributor = c;
        _txtOthers = o;

        foreach (var p in Maintainers)
        {
            if (p.RoleType == "maintainer") p.Role = m;
            else if (p.RoleType == "auth") p.Role = a;
        }

        foreach (var p in Contributors)
        {
            if (p.IsOverflow) p.Name = o;
            else if (p.RoleType == "contributor") p.Role = c;
        }
    }

    private async Task InitializeCreditsAsync()
    {
        try 
        {
            Logger.Info("Github", "Getting HyPrism contributors");

            // --- Maintainers ---
            var yyu = await _gitHubService.GetUserAsync("yyyumeniku");
            var sana = await _gitHubService.GetUserAsync("sanasol");
            
            CreditProfile CreateProfile(GitHubUser? user, string name, string roleType, string initialRole) {
                 var p = new CreditProfile {
                     Name = name,
                     Role = initialRole,
                     RoleType = roleType,
                     ProfileUrl = user?.HtmlUrl ?? "",
                     AvatarUrl = user?.AvatarUrl ?? "",
                     OpenCommand = ReactiveCommand.Create(() => {
                         if(!string.IsNullOrEmpty(user?.HtmlUrl)) _browserService.OpenURL(user.HtmlUrl);
                     })
                 };
                     // Load Avatar Async with resize (96px for 48px HiDPI display)
                     var avatarUrl = user?.AvatarUrl;
                     if (!string.IsNullOrEmpty(avatarUrl))
                     {
                         _ = Task.Run(async () => {
                             var bmp = await _gitHubService.LoadAvatarAsync(avatarUrl);
                             if(bmp != null) 
                             {
                                 Avalonia.Threading.Dispatcher.UIThread.Post(() => p.Avatar = bmp);
                             }
                         });
                     }
                 return p;
            }
    
            Maintainers.Add(CreateProfile(yyu, "yyyumeniku", "maintainer", _txtMaintainer));
            Maintainers.Add(CreateProfile(sana, "sanasol", "auth", _txtAuth));
            
            // --- Contributors ---
            var allContributors = await _gitHubService.GetContributorsAsync();
            
            Logger.Success("Github", "Hyprism developers sucessfully fetched");

            // Filter out specific users if needed (e.g. maintainers)
            var list = allContributors.Where(c => c.Login.ToLower() != "yyyumeniku" && c.Login.ToLower() != "sanasol").ToList();
            
            int maxDisplay = 14; 
            
            List<GitHubUser> usersToShow;
            int overflow = 0;
            
            if (list.Count > maxDisplay)
            {
                 usersToShow = list.Take(maxDisplay - 1).ToList();
                 overflow = list.Count - (maxDisplay - 1);
            }
            else
            {
                 usersToShow = list;
            }
            
            foreach(var c in usersToShow) {
                 Contributors.Add(CreateProfile(c, c.Login, "contributor", _txtContributor));
            }
            
            if (overflow > 0)
            {
                 Contributors.Add(new CreditProfile {
                     Name = _txtOthers,
                     Role = "",
                     IsOverflow = true,
                     OverflowCount = overflow,
                     OpenCommand = ReactiveCommand.Create(() => _browserService.OpenURL("https://github.com/yyyumeniku/HyPrism/graphs/contributors"))
                 });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsViewModel", $"Failed to load credits: {ex.Message}");
        }
    }

    private void SetBackground(string background)
    {
        _settingsService.SetBackgroundMode(background);
        // Force refresh just in case, though the event should handle it
        if (background == "auto")
        {
             // Handled by DashboardViewModel via event
        }
    }

    private void OpenInstanceFolder(InstalledInstance instance)
    {
        if (instance != null) _fileService.OpenFolder(instance.Path);
    }
    
    private void DeleteInstance(InstalledInstance instance)
    {
        if (instance == null) return;
        // TODO: Add confirmation dialog
        _instanceService.DeleteGame(instance.Branch, instance.Version);
        RefreshInstances();
    }

    private async Task BrowseLauncherDataAsync()
    {
        var result = await _fileDialogService.BrowseFolderAsync();
        if (!string.IsNullOrEmpty(result))
        {
            var setResult = await _settingsService.SetLauncherDataDirectoryAsync(result);
            if (!string.IsNullOrEmpty(setResult))
            {
                LauncherDataDirectory = setResult;
            }
        }
    }
    
    private void RandomizeUuid()
    {
        UUID = System.Guid.NewGuid().ToString();
    }
    
    private async Task CopyUuidAsync()
    {
        await _clipboardService.SetTextAsync(UUID);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from events
        _settingsService.OnBackgroundChanged -= OnBackgroundChanged;
        _settingsService.OnAccentColorChanged -= OnAccentColorChanged;

        // Cancel any in-flight version refresh
        _versionRefreshCts?.Cancel();
        _versionRefreshCts?.Dispose();

        // Dispose Rx subscriptions (CombineLatest etc.)
        _subscriptions.Dispose();

        // Dispose all background thumbnails
        foreach (var bg in Backgrounds)
        {
            bg.Thumbnail?.Dispose();
        }
        Backgrounds.Clear();

        // Dispose contributor avatars
        foreach (var credit in Maintainers)
        {
            credit.Avatar?.Dispose();
        }
        Maintainers.Clear();

        foreach (var credit in Contributors)
        {
            credit.Avatar?.Dispose();
        }
        Contributors.Clear();
    }
}
