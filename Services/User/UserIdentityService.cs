using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user identities (UUID and username mappings).
/// Handles UUID generation, username switching, and orphaned skin recovery.
/// </summary>
public class UserIdentityService : IUserIdentityService
{
    // Delegates to access AppService state
    private readonly ConfigService _configService;
    private readonly SkinService _skinService;
    private readonly InstanceService _instanceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserIdentityService"/> class.
    /// </summary>
    /// <param name="configService">The configuration service.</param>
    /// <param name="skinService">The skin management service.</param>
    /// <param name="instanceService">The game instance service.</param>
    public UserIdentityService(
        ConfigService configService,
        SkinService skinService,
        InstanceService instanceService)
    {
        _configService = configService;
        _skinService = skinService;
        _instanceService = instanceService;
    }

    /// <inheritdoc/>
    public string GetUuidForUser(string username)
    {
        var config = _configService.Configuration;
        
        if (string.IsNullOrWhiteSpace(username))
        {
            return config.UUID; // Fallback to legacy single UUID
        }
        
        // Look up UUID from Profiles (case-insensitive)
        var existingProfile = config.Profiles?
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingProfile != null)
        {
            return existingProfile.UUID;
        }
        
        // No existing profile for this username - check current UUID
        if (config.Nick?.Equals(username, StringComparison.OrdinalIgnoreCase) == true)
        {
            return config.UUID;
        }
        
        // Before creating a new one, check if there are orphaned skin files we should adopt
        var orphanedUuid = _skinService.FindOrphanedSkinUuid();
        if (!string.IsNullOrEmpty(orphanedUuid))
        {
            Logger.Info("UUID", $"Recovered orphaned skin UUID for user '{username}': {orphanedUuid}");
            config.UUID = orphanedUuid;
            _configService.SaveConfig();
            return orphanedUuid;
        }
        
        // No orphaned skins found - create a new UUID
        var newUuid = Guid.NewGuid().ToString();
        config.UUID = newUuid;
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Created new UUID for user '{username}': {newUuid}");
        
        return newUuid;
    }

    /// <inheritdoc/>
    public string GetCurrentUuid()
    {
        var config = _configService.Configuration;
        return GetUuidForUser(config.Nick);
    }

    /// <inheritdoc/>
    public List<UuidMapping> GetAllUuidMappings()
    {
        var config = _configService.Configuration;
        var currentNick = config.Nick;
        
        // Build mappings from Profiles
        var mappings = (config.Profiles ?? new List<Profile>())
            .Select(p => new UuidMapping
            {
                Username = p.Name,
                Uuid = p.UUID,
                IsCurrent = p.Name.Equals(currentNick, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
        
        // Add current user if not in any profile
        if (!mappings.Any(m => m.IsCurrent) && !string.IsNullOrEmpty(config.Nick) && !string.IsNullOrEmpty(config.UUID))
        {
            mappings.Insert(0, new UuidMapping
            {
                Username = config.Nick,
                Uuid = config.UUID,
                IsCurrent = true
            });
        }
        
        return mappings;
    }

    /// <inheritdoc/>
    public bool SetUuidForUser(string username, string uuid)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (string.IsNullOrWhiteSpace(uuid)) return false;
        if (!Guid.TryParse(uuid.Trim(), out var parsed)) return false;
        
        var config = _configService.Configuration;
        
        // Find profile with this username (case-insensitive)
        var existingProfile = config.Profiles?
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingProfile != null)
        {
            existingProfile.UUID = parsed.ToString();
        }
        
        // Update legacy UUID if this is the current user
        if (username.Equals(config.Nick, StringComparison.OrdinalIgnoreCase))
        {
            config.UUID = parsed.ToString();
        }
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Set custom UUID for user '{username}': {parsed}");
        return true;
    }

    /// <inheritdoc/>
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
        
        // Find profile with this username (case-insensitive)
        var existingProfile = config.Profiles?
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingProfile != null)
        {
            config.Profiles?.Remove(existingProfile);
            _configService.SaveConfig();
            Logger.Info("UUID", $"Deleted profile for user '{username}'");
            return true;
        }
        
        return false;
    }

    /// <inheritdoc/>
    public string ResetCurrentUserUuid()
    {
        var config = _configService.Configuration;
        var newUuid = Guid.NewGuid().ToString();
        
        // Update profile if exists
        var existingProfile = config.Profiles?
            .FirstOrDefault(p => p.Name.Equals(config.Nick, StringComparison.OrdinalIgnoreCase));
        if (existingProfile != null)
        {
            existingProfile.UUID = newUuid;
        }
        
        config.UUID = newUuid;
        
        _configService.SaveConfig();
        Logger.Info("UUID", $"Reset UUID for current user '{config.Nick}': {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
    public string? SwitchToUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        
        var config = _configService.Configuration;
        
        // Find profile with this username (case-insensitive)
        var existingProfile = config.Profiles?
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
        
        if (existingProfile != null)
        {
            // Switch to existing profile
            config.Nick = existingProfile.Name;
            config.UUID = existingProfile.UUID;
            _configService.SaveConfig();
            Logger.Info("UUID", $"Switched to existing user '{existingProfile.Name}' with UUID {config.UUID}");
            return config.UUID;
        }
        
        // Username doesn't exist - create new UUID
        var newUuid = Guid.NewGuid().ToString();
        config.Nick = username;
        config.UUID = newUuid;
        _configService.SaveConfig();
        Logger.Info("UUID", $"Created new user '{username}' with UUID {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
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
            string? versionPath = null;
            var selected = _instanceService.GetSelectedInstance();
            if (selected != null)
            {
                versionPath = _instanceService.GetInstancePathById(selected.Id)
                              ?? _instanceService.FindExistingInstancePath(selected.Branch, selected.Version);
            }

            if (string.IsNullOrWhiteSpace(versionPath))
            {
                #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
                var branch = UtilityService.NormalizeVersionType(config.VersionType);
                var configuredVersion = config.SelectedVersion;
                #pragma warning restore CS0618
                versionPath = _instanceService.FindExistingInstancePath(branch, configuredVersion)
                              ?? _instanceService.GetInstalledInstances()
                                  .FirstOrDefault(i => i.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase))
                                  ?.Path;
            }

            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("UUID", "No existing instance found, skipping orphaned skin recovery copy");
                return false;
            }

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

    /// <inheritdoc/>
    public string? GetOrphanedSkinUuid() => _skinService.FindOrphanedSkinUuid();
}
