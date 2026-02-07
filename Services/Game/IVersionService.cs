using HyPrism.Models;

namespace HyPrism.Services.Game;

public interface IVersionService
{
    Task<List<int>> GetVersionListAsync(string branch, CancellationToken ct = default);
    bool TryGetCachedVersions(string branch, TimeSpan maxAge, out List<int> versions);
    Task<bool> CheckLatestNeedsUpdateAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);
    Task<VersionStatus> GetLatestVersionStatusAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);
    Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);
    List<int> GetPatchSequence(int fromVersion, int toVersion);
}
