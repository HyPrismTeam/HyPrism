using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using HyPrism.Services;
using HyPrism.Services.Core;
using HyPrism.Models;

namespace HyPrism.UI.Views.NewsView;

/// <summary>
/// Thin wrapper around <see cref="NewsItemResponse"/> that adds a reactive
/// <see cref="IsVisible"/> property for filter-based visibility toggling.
/// This avoids destroying/recreating UI elements (and their loaded Bitmaps)
/// every time the user switches news tabs.
/// </summary>
public class NewsItemViewModel : ReactiveObject
{
    public NewsItemResponse Item { get; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public NewsItemViewModel(NewsItemResponse item) => Item = item;
}

public class NewsViewModel : ReactiveObject, IDisposable
{
    private readonly NewsService _newsService;
    private readonly BrowserService _browserService;
    private bool _disposed;
    
    // Reactive Localization Properties
    public IObservable<string> NewsTitle { get; }
    public IObservable<string> NewsAll { get; }
    public IObservable<string> NewsHytale { get; }
    public IObservable<string> NewsHyPrism { get; }
    public IObservable<string> NewsLoading { get; }
    
    private string _activeFilter = "all"; // "all", "hytale", "hyprism"
    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _activeFilter, value);
            FilterNews();
            this.RaisePropertyChanged(nameof(ActiveTabPosition));
        }
    }
    
    // Tab switcher dimensions and positioning
    public double TabContainerWidth => 380.0;
    public double TabContainerHeight => 44.0;
    public double TabContainerPadding => 4.0;
    public double TabHeight => 36.0;
    
    // Calculate available width for tabs (container - 2*padding)
    private double AvailableWidth => TabContainerWidth - (TabContainerPadding * 2);
    
    // Each tab takes 1/3 of available width
    public double TabWidth => AvailableWidth / 3.0;
    
    public double ActiveTabPosition
    {
        get
        {
            return ActiveFilter.ToLower() switch
            {
                "all" => 0,
                "hytale" => TabWidth,
                "hyprism" => TabWidth * 2,
                _ => 0
            };
        }
    }
    
    /// <summary>
    /// All news items, always present. Filtering is done via <see cref="NewsItemViewModel.IsVisible"/>.
    /// Items are never removed from this collection on tab switch — only on full data refresh.
    /// </summary>
    public ObservableCollection<NewsItemViewModel> News { get; } = new();
    
    // Loading state
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
    
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    // Commands
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<string, Unit> SetFilterCommand { get; }
    public ReactiveCommand<string, Unit> OpenLinkCommand { get; }
    
    public NewsViewModel(NewsService newsService, BrowserService browserService, LocalizationService localizationService)
    {
        _newsService = newsService;
        _browserService = browserService;
        
        // Initialize reactive localization properties
        var loc = localizationService;
        NewsTitle = loc.GetObservable("news.title");
        NewsAll = loc.GetObservable("news.all");
        NewsHytale = loc.GetObservable("news.hytale");
        NewsHyPrism = loc.GetObservable("news.hyprism");
        NewsLoading = loc.GetObservable("news.loading");
        
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadNewsAsync);
        SetFilterCommand = ReactiveCommand.Create<string>(
            filter => 
            { 
                ActiveFilter = filter; 
            },
            Observable.Return(true));
        OpenLinkCommand = ReactiveCommand.Create<string>(url =>
        {
            if (!string.IsNullOrEmpty(url))
            {
                _browserService.OpenURL(url);
            }
        });
        
        _ = LoadNewsAsync();
    }
    
    private async Task LoadNewsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        
        try
        {
            var allNewsItems = await _newsService.GetNewsAsync(30, NewsSource.All);
            
            // Full data refresh — clear and rebuild wrappers
            News.Clear();
            foreach (var item in allNewsItems)
            {
                News.Add(new NewsItemViewModel(item));
            }
            
            // Apply current filter (sets IsVisible on each wrapper)
            FilterNews();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load news: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Toggle visibility on each wrapper instead of clearing/re-adding items.
    /// No UI elements are destroyed or recreated — only IsVisible changes.
    /// This means loaded Bitmaps stay alive and no async race conditions occur.
    /// </summary>
    private void FilterNews()
    {
        foreach (var wrapper in News)
        {
            wrapper.IsVisible = ActiveFilter switch
            {
                "hytale" => wrapper.Item.Source == "hytale",
                "hyprism" => wrapper.Item.Source == "hyprism",
                _ => true
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        RefreshCommand.Dispose();
        SetFilterCommand.Dispose();
        OpenLinkCommand.Dispose();
        
        News.Clear();
    }
}
