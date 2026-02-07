namespace HyPrism.Services.Game;

/// <summary>
/// Handles launching the game process, including client patching, 
/// authentication, and process lifecycle management.
/// </summary>
public interface IGameLauncher
{
    Task LaunchGameAsync(string versionPath, string branch, CancellationToken ct = default);
}
