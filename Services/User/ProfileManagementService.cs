using System.Diagnostics;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Mod;

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
    private bool _profileFolderMigrationAttempted;

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

        EnsureProfileStorageUpgraded();
    }


    /// <inheritdoc/>
    /// <remarks>Filters out any profiles with null/empty names or UUIDs.</remarks>
    public List<Profile> GetProfiles()
    {
        EnsureProfileStorageUpgraded();

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

    private void EnsureProfileStorageUpgraded()
    {
        if (_profileFolderMigrationAttempted)
        {
            return;
        }

        _profileFolderMigrationAttempted = true;

        try
        {
            var config = _configService.Configuration;
            config.Profiles ??= new List<Profile>();

            bool changed = false;
            foreach (var profile in config.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id) || !Guid.TryParse(profile.Id, out _))
                {
                    profile.Id = Guid.NewGuid().ToString();
                    changed = true;
                    Logger.Info("Profile", $"Assigned missing profile ID for '{profile.Name}': {profile.Id}");
                }

                UtilityService.GetProfileFolderPath(_appDir, profile, createIfMissing: false, migrateLegacyByName: true);
            }

            TryMigrateUnresolvedProfileFolders(config.Profiles);

            if (changed)
            {
                _configService.SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Profile storage migration check failed: {ex.Message}");
        }
    }

    private void TryMigrateUnresolvedProfileFolders(List<Profile> profiles)
    {
        var profilesDir = GetProfilesFolder();
        if (!Directory.Exists(profilesDir))
        {
            return;
        }

        foreach (var folder in Directory.GetDirectories(profilesDir))
        {
            var folderName = Path.GetFileName(folder);
            if (Guid.TryParse(folderName, out _))
            {
                continue;
            }

            var matchedByName = profiles.FirstOrDefault(p =>
                string.Equals(UtilityService.SanitizeFileName(p.Name ?? string.Empty), folderName, StringComparison.OrdinalIgnoreCase));

            if (matchedByName != null)
            {
                var target = UtilityService.GetProfileFolderPath(_appDir, matchedByName, createIfMissing: true, migrateLegacyByName: false);
                UtilityService.TryMigrateSpecificProfileFolder(folder, target);
                continue;
            }

            var matchedByMetadata = TryMatchProfileByFolderMetadata(folder, profiles);
            if (matchedByMetadata != null)
            {
                var target = UtilityService.GetProfileFolderPath(_appDir, matchedByMetadata, createIfMissing: true, migrateLegacyByName: false);
                UtilityService.TryMigrateSpecificProfileFolder(folder, target);
                continue;
            }

            Logger.Info("Profile", $"Found unknown profile folder, migration skipped: {folder}");
        }
    }

    private static Profile? TryMatchProfileByFolderMetadata(string folder, List<Profile> profiles)
    {
        try
        {
            var profileJsonPath = Path.Combine(folder, "profile.json");
            if (File.Exists(profileJsonPath))
            {
                var json = File.ReadAllText(profileJsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("uuid", out var uuidEl))
                {
                    var uuid = uuidEl.GetString();
                    if (!string.IsNullOrWhiteSpace(uuid))
                    {
                        var byUuid = profiles.FirstOrDefault(p => string.Equals(p.UUID, uuid, StringComparison.OrdinalIgnoreCase));
                        if (byUuid != null)
                        {
                            return byUuid;
                        }
                    }
                }
            }

            var shFile = Directory.GetFiles(folder, "*.sh").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(shFile))
            {
                var lines = File.ReadAllLines(shFile);
                string? profileId = null;
                string? uuid = null;

                foreach (var line in lines)
                {
                    if (line.Contains("HYPRISM_PROFILE_ID=", StringComparison.Ordinal))
                    {
                        profileId = ExtractQuotedValue(line);
                    }
                    else if (line.Contains("HYPRISM_PROFILE_UUID=", StringComparison.Ordinal))
                    {
                        uuid = ExtractQuotedValue(line);
                    }
                }

                if (!string.IsNullOrWhiteSpace(profileId))
                {
                    var byId = profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
                    if (byId != null)
                    {
                        return byId;
                    }
                }

                if (!string.IsNullOrWhiteSpace(uuid))
                {
                    var byUuid = profiles.FirstOrDefault(p => string.Equals(p.UUID, uuid, StringComparison.OrdinalIgnoreCase));
                    if (byUuid != null)
                    {
                        return byUuid;
                    }
                }
            }
        }
        catch
        {
            // Best effort only
        }

        return null;
    }

    private static string ExtractQuotedValue(string line)
    {
        var start = line.IndexOf('"');
        var end = line.LastIndexOf('"');
        if (start >= 0 && end > start)
        {
            return line.Substring(start + 1, end - start - 1);
        }

        var parts = line.Split('=', 2);
        return parts.Length == 2 ? parts[1].Trim() : string.Empty;
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
            config.Profiles ??= [];
            config.Profiles.Add(profile);
            
            // Auto-activate the first profile created
            if (config.Profiles.Count == 1 || config.ActiveProfileIndex < 0)
            {
                config.ActiveProfileIndex = config.Profiles.Count - 1;
                config.UUID = profile.UUID;
                config.Nick = profile.Name;
                Logger.Info("Profile", $"Auto-activated new profile '{profile.Name}'");
            }
            
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
            
            // Update current UUID and Nick
            config.UUID = profile.UUID;
            config.Nick = profile.Name;
            config.ActiveProfileIndex = index;

            // If user switched to an official profile, force official auth server.
            if (profile.IsOfficial)
            {
                config.AuthDomain = "sessionserver.hytale.com";
                Logger.Info("Profile", "Official profile selected: auth domain switched to sessionserver.hytale.com");
            }
            
            // Ensure mods stay in the active instance folder (no profile symlink/junction)
            EnsureInstanceModsDirectory(profile);
            
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
                var versionPath = TryGetCurrentExistingInstancePath();
                if (string.IsNullOrWhiteSpace(versionPath))
                {
                    Logger.Info("Profile", "No existing instance selected, skipping UserData copy during duplication");
                }
                else
                {
                    var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
                
                    if (Directory.Exists(userDataPath))
                    {
                        var sourceProfileFolder = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                        var sourceUserDataBackup = Path.Combine(sourceProfileFolder, "UserData");
                        var destProfileFolder = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                        var destUserDataBackup = Path.Combine(destProfileFolder, "UserData");
                        
                        // If source profile has a UserData backup, copy it
                        if (Directory.Exists(sourceUserDataBackup))
                        {
                            CopyDirectory(sourceUserDataBackup, destUserDataBackup);
                            Logger.Info("Profile", $"Copied UserData from '{sourceProfile.Name}' to '{newProfile.Name}'");
                        }
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
                var sourceProfileDir = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                
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
                var sourceProfileDir = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                
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
            Profile? profile = null;

            // Try active index first
            if (config.ActiveProfileIndex >= 0 && config.Profiles != null &&
                config.ActiveProfileIndex < config.Profiles.Count)
            {
                profile = config.Profiles[config.ActiveProfileIndex];
            }
            // Fallback: find profile matching current UUID
            else if (config.Profiles != null && config.Profiles.Count > 0 && !string.IsNullOrWhiteSpace(config.UUID))
            {
                var idx = config.Profiles.FindIndex(p => p.UUID == config.UUID);
                if (idx >= 0)
                {
                    profile = config.Profiles[idx];
                    config.ActiveProfileIndex = idx;
                    _configService.SaveConfig();
                    Logger.Info("Profile", $"Auto-activated profile '{profile.Name}' by UUID match");
                }
            }
            // Last resort: activate first profile
            if (profile == null && config.Profiles != null && config.Profiles.Count > 0)
            {
                profile = config.Profiles[0];
                config.ActiveProfileIndex = 0;
                _configService.SaveConfig();
                Logger.Info("Profile", $"Auto-activated first profile '{profile.Name}'");
            }

            if (profile == null)
            {
                Logger.Warning("Profile", "No active profile to open folder for");
                return false;
            }
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                Logger.Info("Profile", $"Created profile folder: {profileDir}");
                
                // Write profile info to the folder so it always has matching data
                try
                {
                    var profileInfo = new
                    {
                        username = profile.Name,
                        uuid = profile.UUID,
                        createdAt = DateTime.UtcNow.ToString("o")
                    };
                    var infoPath = Path.Combine(profileDir, "profile.json");
                    var json = System.Text.Json.JsonSerializer.Serialize(profileInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(infoPath, json);
                    Logger.Info("Profile", $"Created profile info file: {infoPath}");
                }
                catch (Exception infoEx)
                {
                    Logger.Warning("Profile", $"Failed to write profile info: {infoEx.Message}");
                }
            }
            
            // Open folder in file manager (cross-platform) — use ProcessStartInfo to handle paths with spaces
            var psi = new ProcessStartInfo();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = $"\"{profileDir}\"";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = $"\"{profileDir}\"";
            }
            else // Linux
            {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{profileDir}\"";
            }
            psi.UseShellExecute = false;
            Process.Start(psi);
            
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
                Logger.Info("Mods", "No active profile, ensuring instance mods directory without profile linking");
                EnsureInstanceModsDirectory(null);
                return;
            }
            
            var profile = config.Profiles[config.ActiveProfileIndex];

            // Backward compatibility: if old builds created profile symlink/junction,
            // migrate it back into the real instance UserData/Mods directory.
            EnsureInstanceModsDirectory(profile);
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to initialize instance mods directory: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetProfilesFolder()
    {
        return UtilityService.GetProfilesRoot(_appDir);
    }

    // ========== Private Helper Methods ==========
    
    /// <summary>
    /// Gets the path to a profile's mods folder.
    /// </summary>
    private string GetProfileModsFolder(Profile profile)
    {
        var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
        var modsDir = Path.Combine(profileDir, "Mods");
        Directory.CreateDirectory(modsDir);
        return modsDir;
    }
    
    /// <summary>
    /// Ensures the active instance has a real UserData/Mods directory.
    /// If a legacy profile symlink/junction is detected, migrates files back.
    /// </summary>
    private void EnsureInstanceModsDirectory(Profile? profile)
    {
        try
        {
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("Mods", "No existing instance found for mods directory initialization");
                return;
            }

            var userDataPath = Path.Combine(versionPath, "UserData");
            var gameModsPath = Path.Combine(userDataPath, "Mods");

            Directory.CreateDirectory(userDataPath);

            // Handle the case where Hytale created a file named "Mods" instead of a directory
            if (File.Exists(gameModsPath))
            {
                Logger.Warning("Mods",
                    $"Found a file where the Mods directory should be ({gameModsPath}), removing it");
                File.Delete(gameModsPath);
            }

            if (!Directory.Exists(gameModsPath))
            {
                Directory.CreateDirectory(gameModsPath);
                return;
            }

            // Detect legacy symlink/junction created by older builds
            var dirInfo = new DirectoryInfo(gameModsPath);
            bool isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (!isSymlink)
            {
                return;
            }

            string? targetPath = null;
            try
            {
                targetPath = dirInfo.ResolveLinkTarget(true)?.FullName;
            }
            catch
            {
                // ResolveLinkTarget may fail for some junctions; continue best-effort
            }

            var migrationSources = new List<string>();
            if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
            {
                migrationSources.Add(targetPath);
            }

            if (profile != null)
            {
                var profileModsPath = GetProfileModsFolder(profile);
                if (Directory.Exists(profileModsPath) &&
                    !migrationSources.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(profileModsPath), StringComparison.OrdinalIgnoreCase)))
                {
                    migrationSources.Add(profileModsPath);
                }
            }

            Logger.Info("Mods", "Legacy profile mods link detected, migrating back to instance UserData/Mods");

            try
            {
                Directory.Delete(gameModsPath, false);
            }
            catch
            {
                Directory.Delete(gameModsPath, true);
            }

            Directory.CreateDirectory(gameModsPath);

            foreach (var source in migrationSources)
            if (Directory.Exists(source))
            {
                foreach (var file in Directory.GetFiles(source))
                {
                    var destFile = Path.Combine(gameModsPath, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }
            }

            Logger.Success("Mods", $"Using instance-local mods directory: {gameModsPath}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to ensure instance mods directory: {ex.Message}");
        }
    }

    private string? TryGetCurrentExistingInstancePath()
    {
        var config = _configService.Configuration;
        var selected = _instanceService.GetSelectedInstance();
        if (selected != null)
        {
            var selectedPath = _instanceService.GetInstancePathById(selected.Id)
                               ?? _instanceService.FindExistingInstancePath(selected.Branch, selected.Version);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                return selectedPath;
            }
        }

        #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
        var branch = UtilityService.NormalizeVersionType(config.VersionType);
        var configuredVersion = config.SelectedVersion;
        #pragma warning restore CS0618

        var configuredPath = _instanceService.FindExistingInstancePath(branch, configuredVersion);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return _instanceService
            .GetInstalledInstances()
            .FirstOrDefault(i => i.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase))
            ?.Path;
    }
    
    /// <summary>
    /// Saves a profile to disk as a .sh file with name and UUID, plus avatar if available.
    /// </summary>
    private void SaveProfileToDisk(Profile profile)
    {
        try
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
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
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            
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
                var safeName = UtilityService.SanitizeFileName(profileName);
                var profileDirByName = Path.Combine(profilesDir, safeName);
                if (Directory.Exists(profileDirByName))
                {
                    Directory.Delete(profileDirByName, true);
                    Logger.Info("Profile", $"Deleted profile from disk: {profileDirByName}");
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
    /// Recursively copies a directory and all its contents.
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir)
    {
        UtilityService.CopyDirectory(sourceDir, destDir);
    }
}
