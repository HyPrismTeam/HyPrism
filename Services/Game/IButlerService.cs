namespace HyPrism.Services.Game;

public interface IButlerService
{
    string GetButlerPath();
    bool IsButlerInstalled();
    Task<string> EnsureButlerInstalledAsync(Action<int, string>? progressCallback = null);
    Task ApplyPwrAsync(string pwrFile, string targetDir, Action<int, string>? progressCallback = null, CancellationToken externalCancellationToken = default);
}
