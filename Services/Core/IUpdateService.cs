using System.Text.Json;

namespace HyPrism.Services.Core;

public interface IUpdateService
{
    event Action<object>? LauncherUpdateAvailable;
    
    string GetLauncherVersion();
    string GetLauncherBranch();
    Task CheckForLauncherUpdatesAsync();
    Task<bool> UpdateAsync(JsonElement[]? args);
    Task<bool> ForceUpdateLatestAsync(string branch);
    Task<bool> DuplicateLatestAsync(string branch);
    Task<Dictionary<string, object>> WrapperGetStatus();
    Task<bool> WrapperInstallLatest();
    Task<bool> WrapperLaunch();
}
