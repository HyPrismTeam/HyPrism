using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Asset;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user profiles, avatars, nicknames, and UUIDs.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly string _appDataPath;
    private readonly ConfigService _configService;
    private readonly AvatarService? _avatarService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileService"/> class.
    /// </summary>
    /// <param name="appDataPath">The application data directory path.</param>
    /// <param name="configService">The configuration service for accessing user settings.</param>
    /// <param name="avatarService">The avatar service for CachedAvatarPreviews lookups.</param>
    public ProfileService(string appDataPath, ConfigService configService, AvatarService? avatarService = null)
    {
        _appDataPath = appDataPath;
        _configService = configService;
        _avatarService = avatarService;
    }

    /// <inheritdoc/>
    public string GetNick() => _configService.Configuration.Nick;

    /// <inheritdoc/>
    public bool SetNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 16)
            return false;
        
        var config = _configService.Configuration;
        var oldNick = config.Nick;
        
        // Update the active config Nick
        config.Nick = nick;
        
        // Also update the active profile's Name so we don't create a new profile folder
        if (config.Profiles != null && config.ActiveProfileIndex >= 0 && config.ActiveProfileIndex < config.Profiles.Count)
        {
            var activeProfile = config.Profiles[config.ActiveProfileIndex];
            if (activeProfile != null && activeProfile.Name == oldNick)
            {
                activeProfile.Name = nick;
                Logger.Info("Profile", $"Updated active profile name from '{oldNick}' to '{nick}'");
            }
        }
        
        _configService.SaveConfig();
        return true;
    }
    

    /// <inheritdoc/>
    public string GetUUID() => GetCurrentUuid();

    /// <inheritdoc/>
    public bool SetUUID(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return false;
        
        var config = _configService.Configuration;
        var oldUuid = config.UUID;
        
        // Update the active config UUID
        config.UUID = uuid;
        
        // Also update the active profile's UUID so we don't create a new profile
        if (config.Profiles != null && config.ActiveProfileIndex >= 0 && config.ActiveProfileIndex < config.Profiles.Count)
        {
            var activeProfile = config.Profiles[config.ActiveProfileIndex];
            if (activeProfile != null && activeProfile.UUID == oldUuid)
            {
                activeProfile.UUID = uuid;
                Logger.Info("Profile", $"Updated active profile UUID from {oldUuid} to {uuid}");
            }
        }
        
        _configService.SaveConfig();
        return true;
    }

    /// <inheritdoc/>
    public string GetCurrentUuid()
    {
        var uuid = _configService.Configuration.UUID;
        if (string.IsNullOrEmpty(uuid))
        {
            uuid = GenerateNewUuid();
            SetUUID(uuid);
        }
        return uuid;
    }

    /// <inheritdoc/>
    public string GenerateNewUuid()
    {
        return Guid.NewGuid().ToString();
    }

    /// <inheritdoc/>
    public string? GetAvatarPreview()
    {
        var uuid = GetCurrentUuid();
        return GetAvatarPreviewForUUID(uuid);
    }

    /// <inheritdoc/>
    public string? GetAvatarPreviewForUUID(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return null;

            // 1. Check profile folder's avatar.png (most reliable, persisted)
            var profile = _configService.Configuration.Profiles?.FirstOrDefault(p => p.UUID == uuid);
            if (profile != null)
            {
                var profileDir = UtilityService.GetProfileFolderPath(_appDataPath, profile);
                var profileAvatarPath = Path.Combine(profileDir, "avatar.png");

                if (File.Exists(profileAvatarPath) && new FileInfo(profileAvatarPath).Length > 100)
                {
                    var bytes = File.ReadAllBytes(profileAvatarPath);
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
            }

            // 2. Check AvatarBackups (persistent backup from AvatarService)
            if (_avatarService != null)
            {
                var backupPath = _avatarService.GetAvatarBackupPath(uuid);
                if (File.Exists(backupPath) && new FileInfo(backupPath).Length > 100)
                {
                    var bytes = File.ReadAllBytes(backupPath);
                    // Also copy to profile folder for future quick access
                    CopyAvatarToProfile(profile, bytes);
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }

                // 3. Try to backup from CachedAvatarPreviews (game instances)
                if (_avatarService.BackupAvatar(uuid))
                {
                    var freshBackupPath = _avatarService.GetAvatarBackupPath(uuid);
                    if (File.Exists(freshBackupPath) && new FileInfo(freshBackupPath).Length > 100)
                    {
                        var bytes = File.ReadAllBytes(freshBackupPath);
                        CopyAvatarToProfile(profile, bytes);
                        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                    }
                }
            }

            // 4. Legacy fallback: check skins/{uuid}/skin.png|jpg
            var skinsPath = Path.Combine(_appDataPath, "skins", uuid);
            if (Directory.Exists(skinsPath))
            {
                var pngPath = Path.Combine(skinsPath, "skin.png");
                var jpgPath = Path.Combine(skinsPath, "skin.jpg");
                string? skinPath = File.Exists(pngPath) ? pngPath : File.Exists(jpgPath) ? jpgPath : null;
                if (skinPath != null)
                {
                    var bytes = File.ReadAllBytes(skinPath);
                    var mime = skinPath.EndsWith(".png") ? "image/png" : "image/jpeg";
                    return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Avatar", $"Could not load avatar preview for {uuid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies avatar bytes to the profile's root folder for persistent backup.
    /// </summary>
    private void CopyAvatarToProfile(Profile? profile, byte[] avatarBytes)
    {
        if (profile == null) return;
        try
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDataPath, profile);
            Directory.CreateDirectory(profileDir);
            File.WriteAllBytes(Path.Combine(profileDir, "avatar.png"), avatarBytes);
        }
        catch { /* Best effort */ }
    }

    /// <inheritdoc/>
    public bool ClearAvatarCache()
    {
        try
        {
            var skinsPath = Path.Combine(_appDataPath, "skins");
            if (Directory.Exists(skinsPath))
            {
                Directory.Delete(skinsPath, true);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string GetAvatarDirectory()
    {
        var uuid = GetCurrentUuid();
        var skinsPath = Path.Combine(_appDataPath, "skins", uuid);
        
        if (!Directory.Exists(skinsPath))
            Directory.CreateDirectory(skinsPath);
        
        return skinsPath;
    }

    /// <inheritdoc/>
    public bool OpenAvatarDirectory()
    {
        try
        {
            var avatarDir = GetAvatarDirectory();
            
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", avatarDir);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", avatarDir);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", avatarDir);
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public List<Profile> GetProfiles()
    {
        return _configService.Configuration.Profiles ?? new List<Profile>();
    }

    /// <inheritdoc/>
    public bool CreateProfile(string name, string? uuid = null)
    {
        var profiles = GetProfiles();
        var newUuid = uuid ?? GenerateNewUuid();
        
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            UUID = newUuid,
            CreatedAt = DateTime.UtcNow
        };
        
        profiles.Add(profile);
        _configService.Configuration.Profiles = profiles;
        _configService.SaveConfig();
        
        return true;
    }

    /// <inheritdoc/>
    public bool DeleteProfile(string profileId)
    {
        var profiles = GetProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        
        if (profile == null)
            return false;
        
        profiles.Remove(profile);
        _configService.Configuration.Profiles = profiles;
        _configService.SaveConfig();
        
        return true;
    }

    /// <inheritdoc/>
    public bool SwitchProfile(string profileId)
    {
        var profiles = GetProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        
        if (profile == null)
            return false;
        
        SetNick(profile.Name);
        SetUUID(profile.UUID);
        
        return true;
    }

    /// <inheritdoc/>
    public bool SaveCurrentAsProfile()
    {
        var currentNick = GetNick();
        var currentUuid = GetUUID();
        
        var profiles = GetProfiles();
        var existing = profiles.FirstOrDefault(p => p.UUID == currentUuid);
        
        if (existing != null)
        {
            // Update existing profile
            existing.Name = currentNick;
        }
        else
        {
            // Create new profile
            profiles.Add(new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Name = currentNick,
                UUID = currentUuid,
                CreatedAt = DateTime.UtcNow
            });
        }
        
        _configService.Configuration.Profiles = profiles;
        _configService.SaveConfig();
        
        return true;
    }

    /// <inheritdoc/>
    public string GetProfilePath(Profile profile)
    {
        return UtilityService.GetProfileFolderPath(_appDataPath, profile);
    }
}
