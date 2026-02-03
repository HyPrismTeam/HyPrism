using HyPrism.Models;
using HyPrism.Services.Core;
using HyPrism.Services.Game;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user identities (UUID and username mappings).
/// Handles UUID generation, username switching, and orphaned skin recovery.
/// </summary>
public class UserIdentityService
{
    // Delegates to access AppService state
    private readonly ConfigService _configService;
    private readonly SkinService _skinService;
    private readonly InstanceService _instanceService;

    public UserIdentityService(
        ConfigService configService,
        SkinService skinService,
        InstanceService instanceService)
    {
        _configService = configService;
        _skinService = skinService;
        _instanceService = instanceService;
    }

    /// <summary>
    /// Gets UUID for a specific username. Creates new UUID if needed.
    /// Attempts to recover orphaned skin data when creating new UUID.
    /// </summary>
    public string GetUuidForUser(string username)
    {
        var config = _configService.Configuration;
        
        if (string.IsNullOrWhiteSpace(username))
        {
            return config.UUID; // Fallback to legacy single UUID
        }
        
        // Initialize UserUuids if null
        config.UserUuids ??= new Dictionary<string, string>();
        
        // Case-insensitive lookup - find if any existing username matches
        var existingKey = config.UserUuids.Keys
            .FirstOrDefault(k => k.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingKey != null)
        {
            return config.UserUuids[existingKey];
        }
        
        // No existing UUID for this username - before creating a new one,
        // check if there are orphaned skin files we should adopt.
        // This handles the case where config was reset but skin data still exists.
        var orphanedUuid = _skinService.FindOrphanedSkinUuid();
        if (!string.IsNullOrEmpty(orphanedUuid))
        {
            Logger.Info("UUID", $"Recovered orphaned skin UUID for user '{username}': {orphanedUuid}");
            config.UserUuids[username] = orphanedUuid;
            config.UUID = orphanedUuid;
            _configService.SaveConfig();
            return orphanedUuid;
        }
        
        // No orphaned skins found - create a new UUID
        var newUuid = Guid.NewGuid().ToString();
        config.UserUuids[username] = newUuid;
        
        // Also update the legacy UUID field for backwards compatibility
        config.UUID = newUuid;
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Created new UUID for user '{username}': {newUuid}");
        
        return newUuid;
    }
    
    /// <summary>
    /// Gets the UUID for the current user (based on Nick).
    /// </summary>
    public string GetCurrentUuid()
    {
        var config = _configService.Configuration;
        return GetUuidForUser(config.Nick);
    }
    
    /// <summary>
    /// Gets all username->UUID mappings.
    /// Returns a list of objects with username, uuid, and isCurrent properties.
    /// </summary>
    public List<UuidMapping> GetAllUuidMappings()
    {
        var config = _configService.Configuration;
        config.UserUuids ??= new Dictionary<string, string>();
        
        var currentNick = config.Nick;
        return config.UserUuids.Select(kvp => new UuidMapping
        {
            Username = kvp.Key,
            Uuid = kvp.Value,
            IsCurrent = kvp.Key.Equals(currentNick, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }
    
    /// <summary>
    /// Sets a custom UUID for a specific username.
    /// </summary>
    public bool SetUuidForUser(string username, string uuid)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (string.IsNullOrWhiteSpace(uuid)) return false;
        if (!Guid.TryParse(uuid.Trim(), out var parsed)) return false;
        
        var config = _configService.Configuration;
        config.UserUuids ??= new Dictionary<string, string>();
        
        // Remove any existing entry with same username (case-insensitive)
        var existingKey = config.UserUuids.Keys
            .FirstOrDefault(k => k.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
        {
            config.UserUuids.Remove(existingKey);
        }
        
        config.UserUuids[username] = parsed.ToString();
        
        // Update legacy UUID if this is the current user
        if (username.Equals(config.Nick, StringComparison.OrdinalIgnoreCase))
        {
            config.UUID = parsed.ToString();
        }
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Set custom UUID for user '{username}': {parsed}");
        return true;
    }
    
    /// <summary>
    /// Deletes the UUID mapping for a specific username.
    /// Cannot delete the UUID for the current user.
    /// </summary>
    public bool DeleteUuidForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        
        var config = _configService.Configuration;
        
        // Don't allow deleting current user's UUID
        if (username.Equals(config.Nick, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("UUID", $"Cannot delete UUID for current user '{username}'");
            return false;
        }
        
        config.UserUuids ??= new Dictionary<string, string>();
        
        var existingKey = config.UserUuids.Keys
            .FirstOrDefault(k => k.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingKey != null)
        {
            config.UserUuids.Remove(existingKey);
            _configService.SaveConfig();
            Logger.Info("UUID", $"Deleted UUID for user '{username}'");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Generates a new random UUID for the current user.
    /// Warning: This will change the player's identity and they will lose their skin!
    /// </summary>
    public string ResetCurrentUserUuid()
    {
        var config = _configService.Configuration;
        var newUuid = Guid.NewGuid().ToString();
        config.UserUuids ??= new Dictionary<string, string>();
        
        // Remove old entry (case-insensitive)
        var existingKey = config.UserUuids.Keys
            .FirstOrDefault(k => k.Equals(config.Nick, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
        {
            config.UserUuids.Remove(existingKey);
        }
        
        config.UserUuids[config.Nick] = newUuid;
        config.UUID = newUuid;
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Reset UUID for current user '{config.Nick}': {newUuid}");
        return newUuid;
    }
    
    /// <summary>
    /// Switches to an existing username (and its UUID).
    /// Returns the UUID for the username.
    /// </summary>
    public string? SwitchToUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        
        var config = _configService.Configuration;
        config.UserUuids ??= new Dictionary<string, string>();
        
        // Find the username (case-insensitive)
        var existingKey = config.UserUuids.Keys
            .FirstOrDefault(k => k.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingKey != null)
        {
            // Switch to existing username with its UUID
            config.Nick = existingKey; // Use original casing
            config.UUID = config.UserUuids[existingKey];
            _configService.SaveConfig();
            Logger.Info("UUID", $"Switched to existing user '{existingKey}' with UUID {config.UUID}");
            return config.UUID;
        }
        
        // Username doesn't exist in mappings - create new entry
        var newUuid = Guid.NewGuid().ToString();
        config.Nick = username;
        config.UUID = newUuid;
        config.UserUuids[username] = newUuid;
        _configService.SaveConfig();
        Logger.Info("UUID", $"Created new user '{username}' with UUID {newUuid}");
        return newUuid;
    }
    
    /// <summary>
    /// Attempts to recover orphaned skin data and associate it with the current user.
    /// This is useful when a user's config was reset but their skin data still exists.
    /// Returns true if skin data was recovered, false otherwise.
    /// </summary>
    public bool RecoverOrphanedSkinData()
    {
        try
        {
            var config = _configService.Configuration;
            var currentUuid = GetCurrentUuid();
            var orphanedUuid = _skinService.FindOrphanedSkinUuid();
            
            if (string.IsNullOrEmpty(orphanedUuid))
            {
                Logger.Info("UUID", "No orphaned skin data found to recover");
                return false;
            }
            
            // If the current UUID already has a skin, don't overwrite
            var branch = UtilityService.NormalizeVersionType(config.VersionType);
            var versionPath = _instanceService.ResolveInstancePath(branch, 0, true);
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            
            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");
            
            // If current user already has a skin, ask them to use "switch to orphan" instead
            if (File.Exists(currentSkinPath))
            {
                Logger.Info("UUID", $"Current user already has skin data. Use SetUuidForUser to switch to the orphaned UUID: {orphanedUuid}");
                return false;
            }
            
            // Copy orphaned skin to current UUID
            var orphanSkinPath = Path.Combine(skinCacheDir, $"{orphanedUuid}.json");
            if (File.Exists(orphanSkinPath))
            {
                Directory.CreateDirectory(skinCacheDir);
                File.Copy(orphanSkinPath, currentSkinPath, true);
                Logger.Success("UUID", $"Copied orphaned skin from {orphanedUuid} to {currentUuid}");
            }
            
            // Copy orphaned avatar to current UUID
            var orphanAvatarPath = Path.Combine(avatarCacheDir, $"{orphanedUuid}.png");
            var currentAvatarPath = Path.Combine(avatarCacheDir, $"{currentUuid}.png");
            if (File.Exists(orphanAvatarPath))
            {
                Directory.CreateDirectory(avatarCacheDir);
                File.Copy(orphanAvatarPath, currentAvatarPath, true);
                Logger.Success("UUID", $"Copied orphaned avatar from {orphanedUuid} to {currentUuid}");
            }
            
            // Also update the profile if one exists
            var profile = config.Profiles?.FirstOrDefault(p => p.UUID == currentUuid);
            if (profile != null)
            {
                _skinService.BackupProfileSkinData(currentUuid);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UUID", $"Failed to recover orphaned skin data: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Gets the UUID of any orphaned skin found in the game cache.
    /// Returns null if no orphaned skins are found.
    /// </summary>
    public string? GetOrphanedSkinUuid() => _skinService.FindOrphanedSkinUuid();
}
