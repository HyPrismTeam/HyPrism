using Avalonia.Media.Imaging;

namespace HyPrism.Services.Core;

public interface IGitHubService
{
    Task<List<GitHubUser>> GetContributorsAsync();
    Task<GitHubUser?> GetUserAsync(string username);
    Task<Bitmap?> LoadAvatarAsync(string url, int decodeWidth = 96);
}
