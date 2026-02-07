namespace HyPrism.Services.Game;

public interface ILaunchService
{
    Task EnsureJREInstalledAsync(Action<int, string> progressCallback);
    Task<int> GetJavaFeatureVersionAsync(string javaBin);
    Task<bool> SupportsShenandoahAsync(string javaBin);
    string GetJavaPath();
    bool IsVCRedistInstalled();
    Task EnsureVCRedistInstalledAsync(Action<int, string> progressCallback);
}
