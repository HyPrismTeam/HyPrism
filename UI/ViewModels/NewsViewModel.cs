using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using HyPrism.Backend;
using HyPrism.Backend.Services.Core;
using HyPrism.UI.Models;

namespace HyPrism.UI.ViewModels;

public class NewsViewModel : ReactiveObject
{
    private readonly AppService _appService;
    private readonly List<NewsItem> _allNews = new();
    
    private string _activeFilter = "all"; // "all", "hytale", "hyprism"
    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _activeFilter, value);
            FilterNews();            this.RaisePropertyChanged(nameof(ActiveTabPosition));
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
            };        }
    }
    
    // News collection
    public ObservableCollection<NewsItem> News { get; } = new();
    
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
    
    public NewsViewModel(AppService appService)
    {
        _appService = appService;
        
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadNewsAsync);
        SetFilterCommand = ReactiveCommand.Create<string>(
            filter => { ActiveFilter = filter; },
            Observable.Return(true)); // Temporarily allow all
        OpenLinkCommand = ReactiveCommand.Create<string>(url =>
        {
            if (!string.IsNullOrEmpty(url))
            {
                _appService.BrowserOpenURL(url);
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
            var allNewsItems = await _appService.NewsService.GetNewsAsync(30, NewsSource.All);
            
            _allNews.Clear();
            foreach (var item in allNewsItems)
            {
                _allNews.Add(new NewsItem
                {
                    Title = item.Title,
                    Excerpt = item.Excerpt,
                    Url = item.Url,
                    Date = FormatDate(item.Date),
                    Author = item.Author,
                    ImageUrl = item.ImageUrl ?? "",
                    Source = item.Source
                });
            }
            
            // Apply initial filter
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
    
    private void FilterNews()
    {
        News.Clear();
        
        var filtered = ActiveFilter switch
        {
            "hytale" => _allNews.Where(n => n.Source == "hytale"),
            "hyprism" => _allNews.Where(n => n.Source == "hyprism"),
            _ => _allNews
        };
        
        foreach (var item in filtered)
        {
            News.Add(item);
        }
    }
    
    private string FormatDate(string dateString)
    {
        if (DateTime.TryParse(dateString, out var date))
        {
            var now = DateTime.Now;
            var diff = now - date;
            
            if (diff.TotalDays < 1)
                return "Today";
            if (diff.TotalDays < 2)
                return "Yesterday";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} days ago";
            
            return date.ToString("MMM d, yyyy");
        }
        
        return dateString;
    }
}
