namespace HyPrism.Services.Game;

public interface IDownloadService
{
    Task DownloadFileAsync(string url, string destinationPath, Action<int, long, long> progressCallback, CancellationToken ct = default);
    Task<long> GetFileSizeAsync(string url, CancellationToken ct = default);
    Task<bool> FileExistsAsync(string url, CancellationToken ct = default);
}
