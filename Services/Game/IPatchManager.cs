namespace HyPrism.Services.Game;

/// <summary>
/// Manages differential game updates by downloading and applying Butler patches.
/// </summary>
public interface IPatchManager
{
    /// <summary>
    /// Applies differential patches from installedVersion to latestVersion.
    /// </summary>
    Task ApplyDifferentialUpdateAsync(
        string versionPath, 
        string branch,
        int installedVersion, 
        int latestVersion,
        CancellationToken ct = default);
}
