using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Service for handling file downloads with progress tracking.
/// </summary>
public class DownloadService
{
    private readonly HttpClient _httpClient;

    public DownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Download a file with progress reporting.
    /// </summary>
    public async Task DownloadFileAsync(
        string url, 
        string destinationPath, 
        Action<int, long, long> progressCallback, 
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var canReportProgress = totalBytes > 0;
        
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;
            
            if (canReportProgress)
            {
                var progress = (int)((totalRead * 100) / totalBytes);
                progressCallback?.Invoke(progress, totalRead, totalBytes);
            }
        }
        
        Logger.Info("Download", $"Downloaded {totalRead / 1024 / 1024} MB to {destinationPath}");
    }

    /// <summary>
    /// Check file size without downloading.
    /// </summary>
    public async Task<long> GetFileSizeAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return -1;
            }
            
            return response.Content.Headers.ContentLength ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Check if file exists on server.
    /// </summary>
    public async Task<bool> FileExistsAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
