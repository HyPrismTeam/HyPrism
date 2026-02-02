using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HyPrism.Backend.Models;

namespace HyPrism.Backend.Services.Core;

public enum NewsSource
{
    All,
    Hytale,
    HyPrism
}

/// <summary>
/// Fetches news from Hytale API and HyPrism GitHub Releases
/// </summary>
public class NewsService
{
    private readonly HttpClient _httpClient;
    private const string HytaleNewsUrl = "https://hytale.com/api/blog/post/published";
    private const string HyPrismReleasesUrl = "https://api.github.com/repos/yyyumeniku/HyPrism/releases";
    private readonly string _appIconPath;
    
    // Cache for HyPrism news to avoid GitHub API rate limits
    private List<NewsItemResponse>? _hyprismNewsCache;
    private DateTime _hyprismCacheTime = DateTime.MinValue;
    private const int CacheExpirationMinutes = 30;
    
    public NewsService(string appIconPath = "avares://HyPrism/Assets/logo.png")
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism/2.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _appIconPath = appIconPath;
    }
    
    public async Task<List<NewsItemResponse>> GetNewsAsync(int count = 10, NewsSource source = NewsSource.All)
    {
        try
        {
            var allNews = new List<(NewsItemResponse item, DateTime dateTime)>();

            // Fetch Hytale news
            if (source == NewsSource.All || source == NewsSource.Hytale)
            {
                var hytaleNews = await GetHytaleNewsAsync(count);
                allNews.AddRange(hytaleNews.Select(n => (n, ParseDate(n.Date))));
            }

            // Fetch HyPrism news
            if (source == NewsSource.All || source == NewsSource.HyPrism)
            {
                var hyprismNews = await GetHyPrismNewsAsync(count);
                allNews.AddRange(hyprismNews.Select(n => (n, ParseDate(n.Date))));
            }

            // Sort by date descending and take requested count
            var sortedNews = allNews
                .OrderByDescending(x => x.dateTime)
                .Take(count)
                .Select(x => x.item)
                .ToList();

            Logger.Success("News", $"Successfully fetched {sortedNews.Count} news items (source: {source})");
            return sortedNews;
        }
        catch (Exception ex)
        {
            Logger.Error("News", $"Failed to fetch news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    private async Task<List<NewsItemResponse>> GetHytaleNewsAsync(int count)
    {
        try
        {
            Logger.Info("News", "Fetching news from Hytale API...");
            var response = await _httpClient.GetStringAsync(HytaleNewsUrl);
            
            Logger.Info("News", $"Received response, length: {response.Length}");
            
            var jsonDoc = JsonDocument.Parse(response);
            
            JsonElement posts;
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                posts = jsonDoc.RootElement;
            }
            else if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp))
            {
                posts = dataProp;
            }
            else
            {
                Logger.Warning("News", "Unexpected JSON structure from Hytale API");
                return new List<NewsItemResponse>();
            }
            
            var news = new List<NewsItemResponse>();
            
            var itemCount = 0;
            foreach (var post in posts.EnumerateArray())
            {
                if (itemCount >= count) break;
                
                try
                {
                    if (itemCount == 0)
                    {
                        try
                        {
                            Logger.Info("News", $"First post keys: {string.Join(", ", post.EnumerateObject().Select(p => p.Name))}");
                        }
                        catch (Exception logEx)
                        {
                            Logger.Warning("News", $"Failed to log post keys: {logEx.Message}");
                        }
                    }
                    
                    var title = post.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var excerpt = post.TryGetProperty("bodyExcerpt", out var excerptProp) ? excerptProp.GetString() : null;
                    
                    if (string.IsNullOrEmpty(excerpt))
                    {
                        excerpt = post.TryGetProperty("excerpt", out var excerptProp2) ? excerptProp2.GetString() : null;
                    }
                    var slug = post.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() : null;
                    var publishedAt = post.TryGetProperty("publishedAt", out var pubProp) ? pubProp.GetString() : null;
                    
                    string? imageUrl = null;
                    if (post.TryGetProperty("coverImage", out var img))
                    {
                        try
                        {
                            if (img.ValueKind == JsonValueKind.Object)
                            {
                                // New API uses s3Key to build CDN URL
                                if (img.TryGetProperty("s3Key", out var s3KeyProp))
                                {
                                    var s3Key = s3KeyProp.GetString();
                                    if (!string.IsNullOrEmpty(s3Key))
                                    {
                                        imageUrl = $"https://cdn.hytale.com/{s3Key}";
                                    }
                                }
                                // Fallback: try direct url property (very old API structure)
                                else if (img.TryGetProperty("url", out var urlProp))
                                {
                                    imageUrl = urlProp.GetString();
                                }
                            }
                            else if (img.ValueKind == JsonValueKind.String)
                            {
                                // If coverImage is just a string, treat it as s3Key
                                var s3Key = img.GetString();
                                if (!string.IsNullOrEmpty(s3Key))
                                {
                                    imageUrl = $"https://cdn.hytale.com/{s3Key}";
                                }
                            }
                        }
                        catch (Exception imgEx)
                        {
                            Logger.Warning("News", $"Failed to parse coverImage: {imgEx.Message}");
                        }
                    }
                    
                    news.Add(new NewsItemResponse
                    {
                        Title = title ?? "",
                        Excerpt = CleanNewsExcerpt(excerpt, title),
                        Url = !string.IsNullOrEmpty(slug) ? $"https://hytale.com/news/{slug}" : "",
                        Date = publishedAt ?? "",
                        Author = "Hytale Team",
                        ImageUrl = imageUrl,
                        Source = "hytale"
                    });
                    
                    itemCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warning("News", $"Failed to parse news item: {ex.Message}");
                    continue;
                }
            }
            
            return news;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Failed to fetch Hytale news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    private async Task<List<NewsItemResponse>> GetHyPrismNewsAsync(int count)
    {
        // Check cache first
        if (_hyprismNewsCache != null && (DateTime.Now - _hyprismCacheTime).TotalMinutes < CacheExpirationMinutes)
        {
            Logger.Info("News", "Using cached HyPrism news");
            return _hyprismNewsCache.Take(count).ToList();
        }
        
        try
        {
            Logger.Info("News", "Fetching news from HyPrism GitHub...");
            var response = await _httpClient.GetStringAsync(HyPrismReleasesUrl);
            
            var releases = JsonDocument.Parse(response).RootElement;
            var news = new List<NewsItemResponse>();
            
            var itemCount = 0;
            foreach (var release in releases.EnumerateArray())
            {
                if (itemCount >= count) break;
                
                try
                {
                    var name = release.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var tagName = release.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                    var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
                    var htmlUrl = release.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
                    var publishedAt = release.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() : null;
                    
                    var title = !string.IsNullOrEmpty(name) ? name : tagName ?? "HyPrism Release";
                    title = title.Replace("(", "").Replace(")", "").Trim();
                    
                    var excerpt = !string.IsNullOrEmpty(body) 
                        ? body.Split('\n').FirstOrDefault()?.Trim() ?? "Click to see changelog."
                        : "Click to see changelog.";
                    
                    // Remove markdown formatting from excerpt
                    excerpt = Regex.Replace(excerpt, @"[#*_`\[\]]", "");
                    if (excerpt.Length > 100)
                    {
                        excerpt = excerpt.Substring(0, 97) + "...";
                    }
                    
                    news.Add(new NewsItemResponse
                    {
                        Title = $"HyPrism {title} release",
                        Excerpt = excerpt,
                        Url = htmlUrl ?? "https://github.com/yyyumeniku/HyPrism/releases",
                        Date = publishedAt ?? DateTime.Now.ToString("o"),
                        Author = "HyPrism",
                        ImageUrl = _appIconPath,
                        Source = "hyprism"
                    });
                    
                    itemCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warning("News", $"Failed to parse HyPrism release: {ex.Message}");
                    continue;
                }
            }
            
            // Update cache
            _hyprismNewsCache = news;
            _hyprismCacheTime = DateTime.Now;
            
            return news;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Failed to fetch HyPrism news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }
    
    private static DateTime ParseDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;
            
        if (DateTime.TryParse(dateString, out var date))
            return date;
            
        return DateTime.MinValue;
    }
    
    /// <summary>
    /// Cleans news excerpt by removing HTML tags, duplicate title, and date prefixes.
    /// </summary>
    private static string CleanNewsExcerpt(string? rawExcerpt, string? title)
    {
        var excerpt = HttpUtility.HtmlDecode(rawExcerpt ?? "");
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            return "";
        }

        excerpt = Regex.Replace(excerpt, @"<[^>]+>", " ");
        excerpt = Regex.Replace(excerpt, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalizedTitle = Regex.Replace(title.Trim(), @"\s+", " ");
            var escapedTitle = Regex.Escape(normalizedTitle);
            excerpt = Regex.Replace(excerpt, $@"^\s*{escapedTitle}\s*[:\-–—]?\s*", "", RegexOptions.IgnoreCase);
        }

        excerpt = Regex.Replace(excerpt, @"^\s*\p{L}+\s+\d{1,2},\s*\d{4}\s*[–—\-:]?\s*", "", RegexOptions.IgnoreCase);
        excerpt = Regex.Replace(excerpt, @"^\s*\d{1,2}\s+\p{L}+\s+\d{4}\s*[–—\-:]?\s*", "", RegexOptions.IgnoreCase);
        excerpt = Regex.Replace(excerpt, @"^[\-–—:\s]+", "");
        excerpt = Regex.Replace(excerpt, @"(\p{Ll})(\p{Lu})", "$1: $2");

        return excerpt.Trim();
    }
}
