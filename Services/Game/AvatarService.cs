using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Service responsible for managing user avatar cache and preview images.
/// </summary>
public class AvatarService
{
    private readonly InstanceService _instanceService;
    private readonly string _appDir;
    
    public AvatarService(InstanceService instanceService, string appDir)
    {
        _instanceService = instanceService;
        _appDir = appDir;
    }
    
    /// <summary>
    /// Clears the avatar cache for the specified UUID.
    /// Removes avatar from persistent backup and all game instance caches.
    /// </summary>
    public bool ClearAvatarCache(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid)) return false;
            
            // Clear persistent backup
            var persistentPath = Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
            if (File.Exists(persistentPath))
            {
                File.Delete(persistentPath);
                Logger.Info("Avatar", $"Deleted persistent avatar for {uuid}");
            }
            
            // Clear game cache for all instances
            var instanceRoot = _instanceService.GetInstanceRoot();
            if (Directory.Exists(instanceRoot))
            {
                foreach (var branchDir in Directory.GetDirectories(instanceRoot))
                {
                    foreach (var versionDir in Directory.GetDirectories(branchDir))
                    {
                        var avatarPath = Path.Combine(versionDir, "UserData", "CachedAvatarPreviews", $"{uuid}.png");
                        if (File.Exists(avatarPath))
                        {
                            File.Delete(avatarPath);
                            Logger.Info("Avatar", $"Deleted cached avatar at {avatarPath}");
                        }
                    }
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Avatar", $"Failed to clear avatar cache: {ex.Message}");
            return false;
        }
    }
}
