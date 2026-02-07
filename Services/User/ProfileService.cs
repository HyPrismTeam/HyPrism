using HyPrism.Models;
using HyPrism.Services.Core;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user profiles, avatars, nicknames, and UUIDs
/// </summary>
public class ProfileService : IProfileService
{
    private readonly string _appDataPath;
    private readonly ConfigService _configService;
    
    public ProfileService(string appDataPath, ConfigService configService)
    {
        _appDataPath = appDataPath;
        _configService = configService;
    }
    
    // Nickname management
    public string GetNick() => _configService.Configuration.Nick;
    
    public bool SetNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 16)
            return false;
        
        _configService.Configuration.Nick = nick;
        _configService.SaveConfig();
        return true;
    }
    
    // UUID management
    public string GetUUID() => GetCurrentUuid();
    
    public bool SetUUID(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return false;
        
        _configService.Configuration.UUID = uuid;
        _configService.SaveConfig();
        return true;
    }
    
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
    
    public string GenerateNewUuid()
    {
        return Guid.NewGuid().ToString();
    }
    
    // Avatar management
    public string? GetAvatarPreview()
    {
        var uuid = GetCurrentUuid();
        return GetAvatarPreviewForUUID(uuid);
    }
    
    public string? GetAvatarPreviewForUUID(string uuid)
    {
        // Path: AppData/skins/{uuid}/skin.png or skin.jpg
        var skinsPath = Path.Combine(_appDataPath, "skins", uuid);
        
        if (!Directory.Exists(skinsPath))
            return null;
        
        var pngPath = Path.Combine(skinsPath, "skin.png");
        var jpgPath = Path.Combine(skinsPath, "skin.jpg");
        
        if (File.Exists(pngPath))
            return pngPath;
        if (File.Exists(jpgPath))
            return jpgPath;
        
        return null;
    }
    
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
    
    public string GetAvatarDirectory()
    {
        var uuid = GetCurrentUuid();
        var skinsPath = Path.Combine(_appDataPath, "skins", uuid);
        
        if (!Directory.Exists(skinsPath))
            Directory.CreateDirectory(skinsPath);
        
        return skinsPath;
    }
    
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
    
    // Profile list management
    public List<Profile> GetProfiles()
    {
        return _configService.Configuration.Profiles ?? new List<Profile>();
    }
    
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

    public string GetProfilePath(Profile profile)
    {
        var safeName = UtilityService.SanitizeFileName(profile.Name);
        return Path.Combine(_appDataPath, "Profiles", safeName);
    }
}
