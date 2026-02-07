using HyPrism.Models;

namespace HyPrism.Services.User;

public interface ISkinService : IDisposable
{
    void StartSkinProtection(Profile profile, string skinCachePath);
    void StopSkinProtection();
    void TryRecoverOrphanedSkinOnStartup();
    string? FindOrphanedSkinUuid();
    bool RecoverOrphanedSkinData(string currentUuid);
    void BackupProfileSkinData(string uuid);
    void RestoreProfileSkinData(Profile profile);
    void CopyProfileSkinData(string uuid, string profileDir);
}
