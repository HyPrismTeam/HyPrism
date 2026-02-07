using System;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using HyPrism.Models;
using HyPrism.Services.Core;
using HyPrism.Services.User;
using HyPrism.Services;
using ReactiveUI;

namespace HyPrism.UI.Views.ProfileEditorView;

public class ProfileEditorViewModel : ReactiveObject
{
    private readonly IProfileManagementService _profileManagementService;
    private readonly IFileService _fileService;
    private readonly IClipboardService _clipboardService;
    
    private string? _editingProfileId;
    private bool _isCreateMode;
    
    // Properties
    private string _screenTitle = "Create Profile";
    public string ScreenTitle
    {
        get => _screenTitle;
        set => this.RaiseAndSetIfChanged(ref _screenTitle, value);
    }
    
    private string _name = "";
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }
    
    private string _uuid = "";
    public string Uuid
    {
        get => _uuid;
        set => this.RaiseAndSetIfChanged(ref _uuid, value);
    }
    
    private Bitmap? _avatarPreview;
    public Bitmap? AvatarPreview
    {
        get => _avatarPreview;
        set => this.RaiseAndSetIfChanged(ref _avatarPreview, value);
    }
    
    private bool _isSaving;
    public bool IsSaving
    {
        get => _isSaving;
        set => this.RaiseAndSetIfChanged(ref _isSaving, value);
    }
    
    // Events
    public event Action? OnRequestClose;
    public event Action? OnSaved;
    
    // Commands
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ICommand RandomizeUuidCommand { get; }
    public ICommand RandomizeNameCommand { get; }
    public ICommand CopyUuidCommand { get; }
    
    public ProfileEditorViewModel(
        IProfileManagementService profileManagementService,
        IFileService fileService,
        IClipboardService clipboardService)
    {
        _profileManagementService = profileManagementService;
        _fileService = fileService;
        _clipboardService = clipboardService;
        
        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(() => OnRequestClose?.Invoke());
        RandomizeUuidCommand = ReactiveCommand.Create(() => Uuid = Guid.NewGuid().ToString());
        RandomizeNameCommand = ReactiveCommand.Create(GenerateRandomName);
        CopyUuidCommand = ReactiveCommand.CreateFromTask(OnCopyUuid);
    }
    
    private async Task OnCopyUuid()
    {
        await _clipboardService.SetTextAsync(Uuid);
    }

    public void Initialize(Profile? profile)
    {
        if (profile != null)
        {
            _editingProfileId = profile.Id;
            _isCreateMode = false;
            ScreenTitle = "Edit Profile";
            Name = profile.Name;
            Uuid = profile.UUID;
            // TODO: Load avatar
        }
        else
        {
            _editingProfileId = null;
            _isCreateMode = true;
            ScreenTitle = "Create Profile";
            Name = "";
            Uuid = Guid.NewGuid().ToString();
            GenerateRandomName(); // Suggest a name
        }
    }
    
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        if (string.IsNullOrWhiteSpace(Uuid)) return;
        
        IsSaving = true;
        try
        {
            if (_isCreateMode)
            {
                _profileManagementService.CreateProfile(Name, Uuid);
            }
            else if (_editingProfileId != null)
            {
                _profileManagementService.UpdateProfile(_editingProfileId, Name, Uuid);
            }
            OnSaved?.Invoke();
            OnRequestClose?.Invoke();
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    private void GenerateRandomName()
    {
        var adjectives = new[] { "Happy", "Swift", "Brave", "Noble", "Quiet", "Bold", "Lucky", "Epic", "Jolly", "Lunar", "Solar", "Azure", "Royal", "Foxy", "Wacky", "Zesty" };
        var nouns = new[] { "Panda", "Tiger", "Wolf", "Dragon", "Knight", "Ranger", "Mage", "Fox", "Bear", "Eagle", "Hawk", "Lion", "Falcon", "Raven", "Owl", "Shark" };
        var random = new Random();
        var adj = adjectives[random.Next(adjectives.Length)];
        var noun = nouns[random.Next(nouns.Length)];
        var num = random.Next(100, 999);
        Name = $"{adj}{noun}{num}";
    }
}
