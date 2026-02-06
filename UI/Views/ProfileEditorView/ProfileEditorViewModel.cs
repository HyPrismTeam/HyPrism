using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Services.Game;
using HyPrism.Services.User;
using ReactiveUI;

namespace HyPrism.UI.Views.ProfileEditorView;

public class ProfileEditorViewModel : ReactiveObject
{
    private readonly ConfigService _configService;
    private readonly ProfileService _profileService;
    private readonly SkinService _skinService;
    private readonly FileService _fileService;

    private string _uuid = string.Empty;
    private string _username = string.Empty;
    private string? _avatarPreview;
    private bool _isEditingUsername;
    private bool _isEditingUuid;
    private string _editUsername = string.Empty;
    private string _editUuid = string.Empty;
    private bool _isSaving;
    
    // Event for profile updates
    public event Action? ProfileUpdated;
    
    public ProfileEditorViewModel(
        ConfigService configService,
        ProfileService profileService,
        SkinService skinService,
        FileService fileService)
    {
        _configService = configService;
        _profileService = profileService;
        _skinService = skinService;
        _fileService = fileService;
        
        // Commands
        EditUsernameCommand = ReactiveCommand.Create(StartEditingUsername);
        SaveUsernameCommand = ReactiveCommand.CreateFromTask(SaveUsernameAsync);
        CancelUsernameEditCommand = ReactiveCommand.Create(CancelUsernameEdit);
        
        EditUuidCommand = ReactiveCommand.Create(StartEditingUuid);
        SaveUuidCommand = ReactiveCommand.CreateFromTask(SaveUuidAsync);
        CancelUuidEditCommand = ReactiveCommand.Create(CancelUuidEdit);
        
        RandomizeUsernameCommand = ReactiveCommand.Create(RandomizeUsername);
        RandomizeUuidCommand = ReactiveCommand.Create(RandomizeUuid);
        CopyUuidCommand = ReactiveCommand.CreateFromTask(CopyUuidAsync);
        
        OpenAvatarFolderCommand = ReactiveCommand.Create(OpenAvatarFolder);
        RefreshAvatarCommand = ReactiveCommand.CreateFromTask(RefreshAvatarAsync);
        
        CloseCommand = ReactiveCommand.Create(() => { });
    }
    
    // Properties
    public string Uuid
    {
        get => _uuid;
        set => this.RaiseAndSetIfChanged(ref _uuid, value);
    }
    
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }
    
    public string? AvatarPreview
    {
        get => _avatarPreview;
        set => this.RaiseAndSetIfChanged(ref _avatarPreview, value);
    }
    
    public bool IsEditingUsername
    {
        get => _isEditingUsername;
        set => this.RaiseAndSetIfChanged(ref _isEditingUsername, value);
    }
    
    public bool IsEditingUuid
    {
        get => _isEditingUuid;
        set => this.RaiseAndSetIfChanged(ref _isEditingUuid, value);
    }
    
    public string EditUsername
    {
        get => _editUsername;
        set => this.RaiseAndSetIfChanged(ref _editUsername, value);
    }
    
    public string EditUuid
    {
        get => _editUuid;
        set => this.RaiseAndSetIfChanged(ref _editUuid, value);
    }
    
    public bool IsSaving
    {
        get => _isSaving;
        set => this.RaiseAndSetIfChanged(ref _isSaving, value);
    }
    
    // Commands
    public ICommand EditUsernameCommand { get; }
    public ICommand SaveUsernameCommand { get; }
    public ICommand CancelUsernameEditCommand { get; }
    
    public ICommand EditUuidCommand { get; }
    public ICommand SaveUuidCommand { get; }
    public ICommand CancelUuidEditCommand { get; }
    
    public ICommand RandomizeUsernameCommand { get; }
    public ICommand RandomizeUuidCommand { get; }
    public ICommand CopyUuidCommand { get; }
    
    public ICommand OpenAvatarFolderCommand { get; }
    public ICommand RefreshAvatarCommand { get; }
    
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    
    // Methods
    public async Task LoadProfileAsync()
    {
        try
        {
            var config = _configService.Configuration;
            Uuid = config.UUID ?? GenerateUuid();
            Username = config.Nick ?? "HyPrism";
            EditUsername = Username;
            EditUuid = Uuid;
            
            await RefreshAvatarAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load profile: {ex.Message}");
        }
    }
    
    private void StartEditingUsername()
    {
        EditUsername = Username;
        IsEditingUsername = true;
    }
    
    private async Task SaveUsernameAsync()
    {
        var trimmed = EditUsername.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 16)
            return;
        
        IsSaving = true;
        try
        {
            _profileService.SetNick(trimmed);
            Username = trimmed;
            IsEditingUsername = false;
            ProfileUpdated?.Invoke();
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    private void CancelUsernameEdit()
    {
        EditUsername = Username;
        IsEditingUsername = false;
    }
    
    private void StartEditingUuid()
    {
        EditUuid = Uuid;
        IsEditingUuid = true;
    }
    
    private async Task SaveUuidAsync()
    {
        var trimmed = EditUuid.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;
        
        IsSaving = true;
        try
        {
            _profileService.SetUUID(trimmed);
            Uuid = trimmed;
            IsEditingUuid = false;
            
            await RefreshAvatarAsync();
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    private void CancelUuidEdit()
    {
        EditUuid = Uuid;
        IsEditingUuid = false;
    }
    
    private void RandomizeUsername()
    {
        var adjectives = new[] { "Happy", "Swift", "Brave", "Noble", "Quiet", "Bold", "Lucky", "Epic", "Jolly", "Lunar", "Solar", "Azure", "Royal", "Foxy", "Wacky", "Zesty" };
        var nouns = new[] { "Panda", "Tiger", "Wolf", "Dragon", "Knight", "Ranger", "Mage", "Fox", "Bear", "Eagle", "Hawk", "Lion", "Falcon", "Raven", "Owl", "Shark" };
        var random = new Random();
        var adj = adjectives[random.Next(adjectives.Length)];
        var noun = nouns[random.Next(nouns.Length)];
        var num = random.Next(1000, 9999);
        EditUsername = $"{adj}{noun}{num}";
    }
    
    private void RandomizeUuid()
    {
        EditUuid = GenerateUuid();
    }
    
    // Copy UUID to clipboard
    private async Task CopyUuidAsync()
    {
        if (Avalonia.Application.Current != null)
        {
            var topLevel = Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(Uuid);
            }
        }
    }
    
    private void OpenAvatarFolder() => _fileService.OpenAppFolder();
    
    
    private async Task RefreshAvatarAsync()
    {
        // TODO: Implement avatar preview loading
        await Task.CompletedTask;
    }
    
    private string GenerateUuid()
    {
        return Guid.NewGuid().ToString();
    }
}
