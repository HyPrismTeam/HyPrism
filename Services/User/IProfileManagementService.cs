using HyPrism.Models;

namespace HyPrism.Services.User;

public interface IProfileManagementService
{
    List<Profile> GetProfiles();
    int GetActiveProfileIndex();
    Profile? CreateProfile(string name, string uuid);
    bool DeleteProfile(string profileId);
    bool SwitchProfile(int index);
    bool UpdateProfile(string profileId, string? newName, string? newUuid);
    Profile? SaveCurrentAsProfile();
    Profile? DuplicateProfile(string profileId);
    Profile? DuplicateProfileWithoutData(string profileId);
    bool OpenCurrentProfileFolder();
    void InitializeProfileModsSymlink();
    string GetProfilesFolder();
}
