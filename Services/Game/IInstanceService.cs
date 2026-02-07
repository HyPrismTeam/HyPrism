using HyPrism.Models;

namespace HyPrism.Services.Game;

public interface IInstanceService
{
    string GetInstanceRoot();
    string GetBranchPath(string branch);
    string GetInstanceUserDataPath(string versionPath);
    int ResolveVersionOrLatest(string branch, int version);
    string? FindExistingInstancePath(string branch, int version);
    IEnumerable<string> GetInstanceRootsIncludingLegacy();
    string GetLatestInstancePath(string branch);
    string GetLatestInfoPath(string branch);
    LatestInstanceInfo? LoadLatestInfo(string branch);
    void SaveLatestInfo(string branch, int version);
    void MigrateLegacyData();
    bool IsClientPresent(string versionPath);
    bool AreAssetsPresent(string versionPath);
    string GetInstancePath(string branch, int version);
    string ResolveInstancePath(string branch, int version, bool preferExisting);
    bool DeleteGame(string branch, int versionNumber);
    List<InstalledInstance> GetInstalledInstances();
}
