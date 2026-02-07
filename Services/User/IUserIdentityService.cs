using HyPrism.Models;

namespace HyPrism.Services.User;

public interface IUserIdentityService
{
    string GetUuidForUser(string username);
    string GetCurrentUuid();
    List<UuidMapping> GetAllUuidMappings();
    bool SetUuidForUser(string username, string uuid);
    bool DeleteUuidForUser(string username);
    string ResetCurrentUserUuid();
    string? SwitchToUsername(string username);
    bool RecoverOrphanedSkinData();
    string? GetOrphanedSkinUuid();
}
