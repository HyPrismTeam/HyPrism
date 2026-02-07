using HyPrism.Models;

namespace HyPrism.Services.Game;

/// <summary>
/// Orchestrates the download/update/launch workflow.
/// Coordinates between IPatchManager, IGameLauncher, and other services.
/// </summary>
public interface IGameSessionService : IDisposable
{
    Task<DownloadProgress> DownloadAndLaunchAsync(Func<bool>? launchAfterDownloadProvider = null);
    void CancelDownload();
}
