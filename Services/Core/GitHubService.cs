using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using System.Text.Json.Serialization;

namespace HyPrism.Services.Core;

public class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; set; } = "";
    
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private const string RepoOwner = "yyyumeniku";
    private const string RepoName = "HyPrism";

    public GitHubService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Ensure User-Agent is set (required for GitHub API)
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism-Launcher");
        }
    }

    public async Task<List<GitHubUser>> GetContributorsAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contributors?per_page=100";
            return await _httpClient.GetFromJsonAsync<List<GitHubUser>>(url) ?? [];
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.Message.Contains("403"))
            {
                Logger.Warning("GitHub", "Failed to fetch contributors rate limit exceeded");
                return [];
            }
            Logger.Error("GitHub", $"Failed to fetch contributors: {ex}");
            return [];
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to fetch contributors: {ex}");
            return [];
        }
    }

    public async Task<GitHubUser?> GetUserAsync(string username)
    {
        try
        {
            var url = $"https://api.github.com/users/{username}";
            return await _httpClient.GetFromJsonAsync<GitHubUser>(url);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.Message.Contains("403"))
            {
                Logger.Warning("GitHub", $"Failed to fetch user {username} rate limit exceeded");
                return null;
            }
            Logger.Error("GitHub", $"Failed to fetch user {username}: {ex}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to fetch user {username}: {ex}");
            return null;
        }
    }

    public async Task<Bitmap?> LoadAvatarAsync(string url, int decodeWidth = 96)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) return null;
            
            // Append GitHub size parameter to request smaller image from CDN (saves bandwidth + memory)
            var sizedUrl = url.Contains('?') ? $"{url}&s={decodeWidth}" : $"{url}?s={decodeWidth}";
            
            var data = await _httpClient.GetByteArrayAsync(sizedUrl);
            using var stream = new MemoryStream(data);
            // Decode directly to target width to avoid full-size Bitmap allocation
            return Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to load avatar from {url}: {ex}");
            return null;
        }
    }
}
