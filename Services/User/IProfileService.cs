using HyPrism.Models;

namespace HyPrism.Services.User;

public interface IProfileService
{
    string GetNick();
    bool SetNick(string nick);
    string GetUUID();
    bool SetUUID(string uuid);
    string GetCurrentUuid();
    string GenerateNewUuid();
    string? GetAvatarPreview();
    string? GetAvatarPreviewForUUID(string uuid);
    bool ClearAvatarCache();
    string GetAvatarDirectory();
    bool OpenAvatarDirectory();
    List<Profile> GetProfiles();
    bool CreateProfile(string name, string? uuid = null);
    bool DeleteProfile(string profileId);
    bool SwitchProfile(string profileId);
    bool SaveCurrentAsProfile();
    string GetProfilePath(Profile profile);
}
