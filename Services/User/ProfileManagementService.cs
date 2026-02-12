using System.Diagnostics;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core;
using HyPrism.Services.Game;

namespace HyPrism.Services.User;

/// <summary>
/// Manages profile operations: creation, deletion, switching, and profile folder/symlink management.
/// </summary>
public class ProfileManagementService : IProfileManagementService
{
    private readonly string _appDir;
    private readonly ConfigService _configService;
    private readonly SkinService _skinService;
    private readonly InstanceService _instanceService;
    private readonly UserIdentityService _userIdentityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileManagementService"/> class.
    /// </summary>
    /// <param name="appPath">The application path configuration.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="skinService">The skin management service.</param>
    /// <param name="instanceService">The game instance service.</param>
    /// <param name="userIdentityService">The user identity service.</param>
    public ProfileManagementService(
        AppPathConfiguration appPath,
        ConfigService configService,
        SkinService skinService,
        InstanceService instanceService,
        UserIdentityService userIdentityService)
    {
        _appDir = appPath.AppDir;
        _configService = configService;
        _skinService = skinService;
        _instanceService = instanceService;
        _userIdentityService = userIdentityService;
    }

    /// <inheritdoc/>
    /// <remarks>Filters out any profiles with null/empty names or UUIDs.</remarks>
    public List<Profile> GetProfiles()
    {
        var config = _configService.Configuration;
        
        // Clean up any null/empty profiles first
        if (config.Profiles != null)
        {
            var validProfiles = config.Profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.UUID))
                .ToList();
            
            if (validProfiles.Count != config.Profiles.Count)
            {
                Logger.Info("Profile", $"Cleaned up {config.Profiles.Count - validProfiles.Count} invalid profiles");
                config.Profiles = validProfiles;
                _configService.SaveConfig();
            }
        }
        
        var profiles = config.Profiles ?? new List<Profile>();
        Logger.Info("Profile", $"GetProfiles returning {profiles.Count} profiles");
        return profiles;
    }

    /// <inheritdoc/>
    public int GetActiveProfileIndex()
    {
        return _configService.Configuration.ActiveProfileIndex;
    }

    /// <inheritdoc/>
    /// <remarks>Validates name length (1-16 characters) and UUID format before creation.</remarks>
    public Profile? CreateProfile(string name, string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
            {
                Logger.Warning("Profile", $"Cannot create profile with empty name or UUID");
                return null;
            }
            
            // Validate name length (1-16 characters)
            var trimmedName = name.Trim();
            if (trimmedName.Length < 1 || trimmedName.Length > 16)
            {
                Logger.Warning("Profile", $"Invalid name length: {trimmedName.Length} (must be 1-16 chars)");
                return null;
            }
            
            // Validate UUID format
            if (!Guid.TryParse(uuid.Trim(), out var parsedUuid))
            {
                Logger.Warning("Profile", $"Invalid UUID format: {uuid}");
                return null;
            }
            
            var profile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = parsedUuid.ToString(),
                Name = trimmedName,
                CreatedAt = DateTime.UtcNow
            };
            
            var config = _configService.Configuration;
            config.Profiles ??= new List<Profile>();
            config.Profiles.Add(profile);
            Logger.Info("Profile", $"Profile added to list. Total profiles: {config.Profiles.Count}");
            _configService.SaveConfig();
            Logger.Info("Profile", $"Config saved to disk");
            
            // Save profile to disk folder
            SaveProfileToDisk(profile);
            
            Logger.Success("Profile", $"Created profile '{trimmedName}' with UUID {parsedUuid}");
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to create profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Adjusts active profile index if needed after deletion.</remarks>
    public bool DeleteProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }
            
            var config = _configService.Configuration;
            var index = config.Profiles?.FindIndex(p => p.Id == profileId) ?? -1;
            if (index < 0)
            {
                return false;
            }
            
            var profile = config.Profiles![index];
            config.Profiles.RemoveAt(index);
            
            // Adjust active profile index if needed
            if (config.ActiveProfileIndex == index)
            {
                config.ActiveProfileIndex = -1;
            }
            else if (config.ActiveProfileIndex > index)
            {
                config.ActiveProfileIndex--;
            }
            
            _configService.SaveConfig();
            
            // Delete profile folder from disk (pass name for name-based folder)
            DeleteProfileFromDisk(profileId, profile.Name);
            
            Logger.Success("Profile", $"Deleted profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to delete profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Backups current profile's skin data and restores the new profile's skin data.</remarks>
    public bool SwitchProfile(int index)
    {
        try
        {
            var config = _configService.Configuration;
            if (config.Profiles == null || index < 0 || index >= config.Profiles.Count)
            {
                return false;
            }
            
            // First, backup current profile's skin data before switching
            var currentUuid = _userIdentityService.GetCurrentUuid();
            if (!string.IsNullOrWhiteSpace(currentUuid))
            {
                _skinService.BackupProfileSkinData(currentUuid);
            }
            
            var profile = config.Profiles[index];
            
            // Restore the new profile's skin data
            _skinService.RestoreProfileSkinData(profile);
            
            // Update current UUID and Nick - also update UserUuids mapping
            config.UUID = profile.UUID;
            config.Nick = profile.Name;
            config.ActiveProfileIndex = index;
            
            // Ensure the profile's UUID is in the UserUuids mapping
            config.UserUuids ??= new Dictionary<string, string>();
            config.UserUuids[profile.Name] = profile.UUID;
            
            // Switch mods symlink to the new profile's mods folder
            SwitchProfileModsSymlink(profile);
            
            _configService.SaveConfig();
            
            Logger.Success("Profile", $"Switched to profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to switch profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool UpdateProfile(string profileId, string? newName, string? newUuid)
    {
        try
        {
            var config = _configService.Configuration;
            var profile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return false;
            }
            
            if (!string.IsNullOrWhiteSpace(newName))
            {
                profile.Name = newName.Trim();
            }
            
            if (!string.IsNullOrWhiteSpace(newUuid) && Guid.TryParse(newUuid.Trim(), out var parsedUuid))
            {
                profile.UUID = parsedUuid.ToString();
            }
            
            // If this is the active profile, also update current UUID/Nick
            var index = config.Profiles!.FindIndex(p => p.Id == profileId);
            if (index == config.ActiveProfileIndex)
            {
                config.UUID = profile.UUID;
                config.Nick = profile.Name;
            }
            
            _configService.SaveConfig();
            
            // Update profile on disk
            UpdateProfileOnDisk(profile);
            
            Logger.Success("Profile", $"Updated profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to update profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Updates existing profile if UUID already exists, otherwise creates new.</remarks>
    public Profile? SaveCurrentAsProfile()
    {
        var config = _configService.Configuration;
        // Use Config.UUID directly instead of GetUuidForUser(Config.Nick).
        // GetUuidForUser generates a NEW UUID when the nick changes (since the new nick
        // has no mapping yet), which causes a new profile to be created instead of updating
        // the existing one.
        var uuid = config.UUID;
        var name = config.Nick;
        
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        
        // Ensure the UserUuids mapping is up-to-date for the current nick
        config.UserUuids ??= new Dictionary<string, string>();
        // Remove any stale mapping pointing to this UUID (old nick)
        var staleKey = config.UserUuids.FirstOrDefault(kvp => 
            kvp.Value.Equals(uuid, StringComparison.OrdinalIgnoreCase) && 
            !kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
        if (staleKey != null)
        {
            config.UserUuids.Remove(staleKey);
            Logger.Info("Profile", $"Removed stale UUID mapping for old nick '{staleKey}'");
        }
        // Set mapping for current nick → current UUID
        config.UserUuids[name] = uuid;
        
        // Check if a profile with this UUID already exists
        var existing = config.Profiles?.FirstOrDefault(p => p.UUID == uuid);
        if (existing != null)
        {
            // Update existing profile
            existing.Name = name;
            _configService.SaveConfig();
            UpdateProfileOnDisk(existing);
            return existing;
        }
        
        // Create new profile
        return CreateProfile(name, uuid);
    }

    /// <inheritdoc/>
    /// <remarks>Copies UserData folder, mods folder, and skin data from the source profile.</remarks>
    public Profile? DuplicateProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }
            
            var config = _configService.Configuration;
            var sourceProfile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }
            
            // Generate new UUID and name with " Copy" suffix
            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            
            // Ensure name is unique
            int copyCount = 1;
            while (config.Profiles != null && config.Profiles.Any(p => p.Name == newName))
            {
                copyCount++;
                newName = $"{sourceProfile.Name} Copy {copyCount}";
            }
            
            // Create new profile
            var newProfile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = newUuid,
                Name = newName,
                CreatedAt = DateTime.UtcNow
            };
            
            config.Profiles ??= new List<Profile>();
            config.Profiles.Add(newProfile);
            _configService.SaveConfig();
            
            // Save profile to disk
            SaveProfileToDisk(newProfile);
            
            // Copy source profile's mods folder to new profile
            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);
                
                if (Directory.Exists(sourceModsPath))
                {
                    CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }
            
            // Copy UserData from source profile if it exists
            try
            {
                var branch = UtilityService.NormalizeVersionType(config.VersionType);
                var versionPath = _instanceService.ResolveInstancePath(branch, 0, true);
                var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
                
                if (Directory.Exists(userDataPath))
                {
                    var profilesDir = GetProfilesFolder();
                    var sourceProfileFolder = Path.Combine(profilesDir, SanitizeFileName(sourceProfile.Name));
                    var sourceUserDataBackup = Path.Combine(sourceProfileFolder, "UserData");
                    var destProfileFolder = Path.Combine(profilesDir, SanitizeFileName(newProfile.Name));
                    var destUserDataBackup = Path.Combine(destProfileFolder, "UserData");
                    
                    // If source profile has a UserData backup, copy it
                    if (Directory.Exists(sourceUserDataBackup))
                    {
                        CopyDirectory(sourceUserDataBackup, destUserDataBackup);
                        Logger.Info("Profile", $"Copied UserData from '{sourceProfile.Name}' to '{newProfile.Name}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy UserData during duplication: {ex.Message}");
            }
            
            // Copy skin/avatar data
            try
            {
                var profilesDir = GetProfilesFolder();
                var sourceProfileDir = Path.Combine(profilesDir, SanitizeFileName(sourceProfile.Name));
                var destProfileDir = Path.Combine(profilesDir, SanitizeFileName(newProfile.Name));
                
                // Copy skin.png if exists
                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }
                
                // Copy avatar.png if exists
                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }
            
            Logger.Success("Profile", $"Duplicated profile '{sourceProfile.Name}' → '{newProfile.Name}'");
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Copies mods and skin/avatar but NOT UserData folder.</remarks>
    public Profile? DuplicateProfileWithoutData(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }
            
            var config = _configService.Configuration;
            var sourceProfile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }
            
            // Generate new UUID and name with " Copy" suffix
            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            
            // Ensure name is unique
            int copyCount = 1;
            while (config.Profiles != null && config.Profiles.Any(p => p.Name == newName))
            {
                copyCount++;
                newName = $"{sourceProfile.Name} Copy {copyCount}";
            }
            
            // Create new profile
            var newProfile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = newUuid,
                Name = newName,
                CreatedAt = DateTime.UtcNow
            };
            
            config.Profiles ??= new List<Profile>();
            config.Profiles.Add(newProfile);
            _configService.SaveConfig();
            
            // Save profile to disk
            SaveProfileToDisk(newProfile);
            
            // Copy source profile's mods folder to new profile
            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);
                
                if (Directory.Exists(sourceModsPath))
                {
                    CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }
            
            // Copy skin/avatar data (but NOT UserData)
            try
            {
                var profilesDir = GetProfilesFolder();
                var sourceProfileDir = Path.Combine(profilesDir, SanitizeFileName(sourceProfile.Name));
                var destProfileDir = Path.Combine(profilesDir, SanitizeFileName(newProfile.Name));
                
                // Copy skin.png if exists
                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }
                
                // Copy avatar.png if exists
                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }
            
            Logger.Success("Profile", $"Duplicated profile (without UserData) '{sourceProfile.Name}' → '{newProfile.Name}'");
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile without data: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public bool OpenCurrentProfileFolder()
    {
        try
        {
            var config = _configService.Configuration;
            if (config.ActiveProfileIndex < 0 || config.Profiles == null ||
                config.ActiveProfileIndex >= config.Profiles.Count)
            {
                Logger.Warning("Profile", "No active profile to open folder for");
                return false;
            }
            
            var profile = config.Profiles[config.ActiveProfileIndex];
            var profilesDir = GetProfilesFolder();
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                Logger.Info("Profile", $"Created profile folder: {profileDir}");
            }
            
            // Open folder in file manager (cross-platform)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", profileDir);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", profileDir);
            }
            else // Linux
            {
                Process.Start("xdg-open", profileDir);
            }
            
            Logger.Success("Profile", $"Opened profile folder: {profileDir}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to open profile folder: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void InitializeProfileModsSymlink()
    {
        try
        {
            var config = _configService.Configuration;
            if (config.ActiveProfileIndex < 0 || config.Profiles == null || 
                config.ActiveProfileIndex >= config.Profiles.Count)
            {
                Logger.Info("Mods", "No active profile, skipping symlink initialization");
                return;
            }
            
            var profile = config.Profiles[config.ActiveProfileIndex];
            
            // Check if the game instance folder exists
            var branch = UtilityService.NormalizeVersionType(config.VersionType);
            var versionPath = _instanceService.ResolveInstancePath(branch, 0, true);
            var userDataPath = Path.Combine(versionPath, "UserData");
            var gameModsPath = Path.Combine(userDataPath, "Mods");
            
            // Check if symlink already exists and points to correct profile
            if (Directory.Exists(gameModsPath))
            {
                var dirInfo = new DirectoryInfo(gameModsPath);
                bool isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
                
                if (isSymlink)
                {
                    // Symlink exists, verify it points to correct profile
                    var profileModsPath = GetProfileModsFolder(profile);
                    
                    // Get symlink target
                    string? targetPath = null;
                    try
                    {
                        targetPath = dirInfo.ResolveLinkTarget(true)?.FullName;
                    }
                    catch { /* Ignore errors getting target */ }
                    
                    if (targetPath != null && Path.GetFullPath(targetPath) == Path.GetFullPath(profileModsPath))
                    {
                        Logger.Info("Mods", $"Mods symlink already points to active profile: {profile.Name}");
                        return;
                    }
                    
                    // Wrong target, recreate
                    Logger.Info("Mods", "Mods symlink points to wrong profile, updating...");
                }
            }
            
            // Create/update symlink
            SwitchProfileModsSymlink(profile);
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to initialize profile mods symlink: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetProfilesFolder()
    {
        var profilesDir = Path.Combine(_appDir, "Profiles");
        Directory.CreateDirectory(profilesDir);
        return profilesDir;
    }

    // ========== Private Helper Methods ==========
    
    /// <summary>
    /// Gets the path to a profile's mods folder.
    /// </summary>
    private string GetProfileModsFolder(Profile profile)
    {
        var profilesDir = GetProfilesFolder();
        var safeName = SanitizeFileName(profile.Name);
        var profileDir = Path.Combine(profilesDir, safeName);
        var modsDir = Path.Combine(profileDir, "Mods");
        Directory.CreateDirectory(modsDir);
        return modsDir;
    }
    
    /// <summary>
    /// Switches the mods symlink to point to the new profile's mods folder.
    /// On Windows, creates a directory junction. On Unix, creates a symlink.
    /// </summary>
    private void SwitchProfileModsSymlink(Profile profile)
    {
        try
        {
            var config = _configService.Configuration;
            // Get the game's UserData/Mods path
            var branch = UtilityService.NormalizeVersionType(config.VersionType);
            var versionPath = _instanceService.ResolveInstancePath(branch, 0, true);
            var userDataPath = Path.Combine(versionPath, "UserData");
            var gameModsPath = Path.Combine(userDataPath, "Mods");
            
            // Get the profile's mods folder
            var profileModsPath = GetProfileModsFolder(profile);
            
            // If the game mods path exists and is not a symlink, migrate existing mods
            if (Directory.Exists(gameModsPath))
            {
                var dirInfo = new DirectoryInfo(gameModsPath);
                bool isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
                
                if (!isSymlink)
                {
                    // Real directory - migrate mods to profile folder then delete
                    Logger.Info("Mods", "Migrating existing mods to profile folder...");
                    
                    foreach (var file in Directory.GetFiles(gameModsPath))
                    {
                        var destFile = Path.Combine(profileModsPath, Path.GetFileName(file));
                        if (!File.Exists(destFile))
                        {
                            File.Copy(file, destFile);
                        }
                    }
                    
                    // Also copy manifest.json if it exists
                    var manifestPath = Path.Combine(gameModsPath, "manifest.json");
                    var destManifest = Path.Combine(profileModsPath, "manifest.json");
                    if (File.Exists(manifestPath) && !File.Exists(destManifest))
                    {
                        File.Copy(manifestPath, destManifest);
                    }
                    
                    // Delete the original directory
                    Directory.Delete(gameModsPath, true);
                    Logger.Success("Mods", $"Migrated mods from game folder to profile: {profile.Name}");
                }
                else
                {
                    // It's already a symlink - just delete it
                    Directory.Delete(gameModsPath, false);
                }
            }
            
            // Create the symlink/junction
            Directory.CreateDirectory(userDataPath);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, create a directory junction (works without admin rights)
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{gameModsPath}\" \"{profileModsPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = Process.Start(processInfo);
                process?.WaitForExit(5000);
                
                if (process?.ExitCode != 0)
                {
                    Logger.Warning("Mods", "Failed to create junction, falling back to directory copy");
                    // Fallback: just create the directory
                    Directory.CreateDirectory(gameModsPath);
                }
                else
                {
                    Logger.Success("Mods", $"Created junction: {gameModsPath} -> {profileModsPath}");
                }
            }
            else
            {
                // On Unix (macOS/Linux), create a symbolic link
                var processInfo = new ProcessStartInfo
                {
                    FileName = "ln",
                    Arguments = $"-s \"{profileModsPath}\" \"{gameModsPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = Process.Start(processInfo);
                process?.WaitForExit(5000);
                
                if (process?.ExitCode != 0)
                {
                    Logger.Warning("Mods", "Failed to create symlink, falling back to directory copy");
                    Directory.CreateDirectory(gameModsPath);
                }
                else
                {
                    Logger.Success("Mods", $"Created symlink: {gameModsPath} -> {profileModsPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to switch profile mods symlink: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Saves a profile to disk as a .sh file with name and UUID, plus avatar if available.
    /// </summary>
    private void SaveProfileToDisk(Profile profile)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            // Use profile name as folder name (sanitize for filesystem)
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            Directory.CreateDirectory(profileDir);
            
            // Create the Mods folder for this profile
            var modsDir = Path.Combine(profileDir, "Mods");
            Directory.CreateDirectory(modsDir);
            
            // Create the shell script with profile info
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            // Copy skin and avatar from game cache to profile folder
            _skinService.CopyProfileSkinData(profile.UUID, profileDir);
            
            Logger.Info("Profile", $"Saved profile to disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save profile to disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Updates a profile's disk files when it's modified.
    /// </summary>
    private void UpdateProfileOnDisk(Profile profile)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            var safeName = SanitizeFileName(profile.Name);
            var profileDir = Path.Combine(profilesDir, safeName);
            
            // Also check for old folder with ID and rename it
            var oldProfileDir = Path.Combine(profilesDir, profile.Id);
            if (Directory.Exists(oldProfileDir) && !Directory.Exists(profileDir))
            {
                Directory.Move(oldProfileDir, profileDir);
            }
            
            if (!Directory.Exists(profileDir))
            {
                SaveProfileToDisk(profile);
                return;
            }
            
            // Remove old .sh files
            foreach (var oldSh in Directory.GetFiles(profileDir, "*.sh"))
            {
                File.Delete(oldSh);
            }
            
            // Create new .sh file
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}
# Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            Logger.Info("Profile", $"Updated profile on disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to update profile on disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Deletes a profile's disk folder.
    /// </summary>
    private void DeleteProfileFromDisk(string profileId, string? profileName = null)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            
            // Try to delete by name first if provided
            if (!string.IsNullOrEmpty(profileName))
            {
                var safeName = SanitizeFileName(profileName);
                var profileDirByName = Path.Combine(profilesDir, safeName);
                if (Directory.Exists(profileDirByName))
                {
                    Directory.Delete(profileDirByName, true);
                    Logger.Info("Profile", $"Deleted profile from disk: {profileDirByName}");
                    return;
                }
            }
            
            // Fallback to ID-based folder (for migration)
            var profileDir = Path.Combine(profilesDir, profileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
                Logger.Info("Profile", $"Deleted profile from disk: {profileDir}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to delete profile from disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sanitizes a string to be safe for use as a filename.
    /// </summary>
    private string SanitizeFileName(string name)
    {
        return UtilityService.SanitizeFileName(name);
    }
    
    /// <summary>
    /// Recursively copies a directory and all its contents.
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir)
    {
        UtilityService.CopyDirectory(sourceDir, destDir);
    }
}
