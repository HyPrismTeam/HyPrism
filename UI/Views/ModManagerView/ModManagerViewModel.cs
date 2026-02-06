using ReactiveUI;
using HyPrism.Services.Game;
using HyPrism.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Reactive;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Linq;
using System;
using HyPrism.Services.Core;

namespace HyPrism.UI.Views.ModManagerView;

public class ModManagerViewModel : ReactiveObject
{
    private readonly ModService _modService;
    private readonly InstanceService _instanceService;
    private readonly string _branch;
    private readonly int _version;

    // Loading State
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
    
    private string _loadingText = "Loading...";
    public string LoadingText
    {
        get => _loadingText;
        set => this.RaiseAndSetIfChanged(ref _loadingText, value);
    }

    // Tabs
    private string _activeTab = "installed";
    public string ActiveTab
    {
        get => _activeTab;
        set 
        {
            this.RaiseAndSetIfChanged(ref _activeTab, value);
            this.RaisePropertyChanged(nameof(IsInstalledTab));
            this.RaisePropertyChanged(nameof(IsBrowseTab));
            
            // Allow tab switching even if details are open (closes details)
            IsDetailsOpen = false; 

            if (value == "installed")
                _ = LoadInstalledMods();
            else if (value == "browse" && SearchResults.Count == 0) 
                 _ = SearchModsAsync();
        }
    }

    public bool IsInstalledTab => ActiveTab == "installed";
    public bool IsBrowseTab => ActiveTab == "browse";

    // Installed Mods
    private ObservableCollection<InstalledMod> _installedMods = new();
    public ObservableCollection<InstalledMod> InstalledMods
    {
        get => _installedMods;
        set => this.RaiseAndSetIfChanged(ref _installedMods, value);
    }

    // Browse Mods
    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
    }
    
    private ObservableCollection<ModInfo> _searchResults = new();
    public ObservableCollection<ModInfo> SearchResults
    {
        get => _searchResults;
        set => this.RaiseAndSetIfChanged(ref _searchResults, value);
    }
    
    // Categories
    public ObservableCollection<ModCategory> Categories { get; } = new();
    
    private ModCategory? _selectedCategory;
    public ModCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCategory, value);
            _searchQuery = "";
            this.RaisePropertyChanged(nameof(SearchQuery));
            _currentPage = 0;
            _ = SearchModsAsync();
        }
    }

    private int _currentPage = 0;
    private int _totalResults = 0;
    public bool CanLoadMore => SearchResults.Count < _totalResults;

    // Details View
    private bool _isDetailsOpen;
    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => this.RaiseAndSetIfChanged(ref _isDetailsOpen, value);
    }
    
    private ModInfo? _selectedMod;
    public ModInfo? SelectedMod
    {
        get => _selectedMod;
        set => this.RaiseAndSetIfChanged(ref _selectedMod, value);
    }
    
    private ObservableCollection<ModFileInfo> _selectedModFiles = new();
    public ObservableCollection<ModFileInfo> SelectedModFiles
    {
        get => _selectedModFiles;
        set => this.RaiseAndSetIfChanged(ref _selectedModFiles, value);
    }
    
    // Commands
    public ReactiveCommand<string, Unit> SwitchTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadMoreCommand { get; }
    public ReactiveCommand<ModInfo, Unit> OpenDetailsCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseDetailsCommand { get; }
    
    // Action Commands
    public ReactiveCommand<ModFileInfo, Unit> InstallModCommand { get; }
    public ReactiveCommand<InstalledMod, Unit> UninstallModCommand { get; }
    public ReactiveCommand<InstalledMod, Unit> ToggleModCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInstalledCommand { get; }

    public ModManagerViewModel(ModService modService, InstanceService instanceService, string branch, int version)
    {
        _modService = modService;
        _instanceService = instanceService;
        _branch = branch;
        _version = version;
        
        SwitchTabCommand = ReactiveCommand.Create<string>(tab => ActiveTab = tab);
        CloseCommand = ReactiveCommand.Create(() => {});
        
        SearchCommand = ReactiveCommand.CreateFromTask(async () => 
        {
            _currentPage = 0;
            await SearchModsAsync();
        });
        
        LoadMoreCommand = ReactiveCommand.CreateFromTask(async () => 
        {
            _currentPage++;
            await SearchModsAsync(true);
        });
        
        OpenDetailsCommand = ReactiveCommand.CreateFromTask<ModInfo>(OpenDetailsAsync);
        CloseDetailsCommand = ReactiveCommand.Create(() => { IsDetailsOpen = false; });
        
        InstallModCommand = ReactiveCommand.CreateFromTask<ModFileInfo>(InstallModAsync);
        UninstallModCommand = ReactiveCommand.CreateFromTask<InstalledMod>(UninstallModAsync);
        ToggleModCommand = ReactiveCommand.CreateFromTask<InstalledMod>(ToggleModAsync);
        RefreshInstalledCommand = ReactiveCommand.CreateFromTask(async () => await LoadInstalledMods());
        
        // Initial Load
        _ = LoadCategories();
        _ = LoadInstalledMods();
    }
    
    private async Task LoadCategories()
    {
        try
        {
            var cats = await _modService.GetModCategoriesAsync();
            await Dispatcher.UIThread.InvokeAsync(() => 
            {
                Categories.Clear();
                Categories.Add(new ModCategory { Id = 0, Name = "All" });
                foreach(var c in cats) Categories.Add(c);
                // Don't select default, allow null/0 to be "All"
                SelectedCategory = Categories.First();
            });
        }
        catch(Exception ex)
        {
             Logger.Error("ModManager", $"Failed to load categories: {ex}");
        }
    }
    
    public async Task SearchModsAsync(bool append = false)
    {
        if (!append) IsLoading = true;
        
        try 
        {
             string[] categoryFilter = SelectedCategory != null && SelectedCategory.Id != 0 
                ? new[] { SelectedCategory.Name } 
                : Array.Empty<string>();
             
             var result = await _modService.SearchModsAsync(_searchQuery, _currentPage, 20, categoryFilter, 2, 1);
             _totalResults = result.TotalCount;
             
             await Dispatcher.UIThread.InvokeAsync(() => 
             {
                 if (!append) SearchResults.Clear();
                 foreach(var m in result.Mods) SearchResults.Add(m);
                 this.RaisePropertyChanged(nameof(CanLoadMore));
             });
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Error searching mods: {ex}");
        }
        finally 
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    public async Task LoadInstalledMods()
    {
        IsLoading = true;
        try 
        {
             var instancePath = _instanceService.ResolveInstancePath(_branch, _version, false);
             var mods = await Task.Run(() => _modService.GetInstanceInstalledMods(instancePath));
             await Dispatcher.UIThread.InvokeAsync(() => 
             {
                 InstalledMods = new ObservableCollection<InstalledMod>(mods);
             });
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Error loading mods: {ex}");
        }
        finally 
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }
    
    private async Task OpenDetailsAsync(ModInfo mod)
    {
        IsLoading = true;
        SelectedMod = mod;
        IsDetailsOpen = true;
        SelectedModFiles.Clear();
        
        try
        {
            var filesResult = await _modService.GetModFilesAsync(mod.Id, 0, 20);
            await Dispatcher.UIThread.InvokeAsync(() => 
            {
                SelectedModFiles = new ObservableCollection<ModFileInfo>(filesResult.Files);
            });
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Error loading mod files: {ex}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }
    
    private async Task InstallModAsync(ModFileInfo file)
    {
        if (file == null) return;
        
        IsLoading = true;
        LoadingText = $"Installing {file.DisplayName}...";
        
        try
        {
            var instancePath = _instanceService.ResolveInstancePath(_branch, _version, true);
            
            bool success = await _modService.InstallModFileToInstanceAsync(
                file.ModId, 
                file.Id, 
                instancePath,
                (status, msg) => 
                {
                    // Dispatcher.UIThread.Post(() => LoadingText = msg);
                });
                
            if (success)
            {
                await LoadInstalledMods();
                IsDetailsOpen = false;
                ActiveTab = "installed";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Install failed: {ex}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => 
            {
                IsLoading = false;
                LoadingText = "Loading...";
            });
        }
    }
    
    private async Task UninstallModAsync(InstalledMod mod)
    {
        if (mod == null) return;
        
        IsLoading = true;
        try 
        {
             var instancePath = _instanceService.ResolveInstancePath(_branch, _version, false);
             var modsPath = System.IO.Path.Combine(instancePath, "Client", "mods");
             var fileName = !string.IsNullOrEmpty(mod.FileName) ? mod.FileName : $"{mod.Slug ?? mod.Id}.jar";
             var filePath = System.IO.Path.Combine(modsPath, fileName);
             
             if (System.IO.File.Exists(filePath))
                 System.IO.File.Delete(filePath);
                 
             InstalledMods.Remove(mod);
             await _modService.SaveInstanceModsAsync(instancePath, InstalledMods.ToList());
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Uninstall failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task ToggleModAsync(InstalledMod mod)
    {
        mod.Enabled = !mod.Enabled;
        // Since InstalledMod doesn't implement INotifyPropertyChanged, we force UI update
        // by replacing the item ideally, or the View should bind to Enabled if it were observable.
        // For quick fix:
        var idx = InstalledMods.IndexOf(mod);
        if (idx >= 0) {
            InstalledMods.RemoveAt(idx);
            InstalledMods.Insert(idx, mod);
        }
        
        var instancePath = _instanceService.ResolveInstancePath(_branch, _version, false);
        await _modService.SaveInstanceModsAsync(instancePath, InstalledMods.ToList());
    }
}
