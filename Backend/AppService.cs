using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HyPrism.Backend.Services.Core;
using HyPrism.Backend.Services.Game;
using HyPrism.Backend.Services.User;
using HyPrism.Backend.Models;

namespace HyPrism.Backend;

public class AppService : IDisposable
{
    private readonly string _configPath;
    private readonly string _appDir;
    private Config _config;
    private Process? _gameProcess;
    private readonly ButlerService _butlerService;
    private readonly DiscordService _discordService;
    private CancellationTokenSource? _downloadCts;
    private bool _disposed;
    
    // New services
    private readonly ConfigService _configService;
    private readonly ProfileService _profileService;
    private readonly NewsService _newsService;
    private readonly VersionService _versionService;
    private readonly DownloadService _downloadService;
    private readonly ModService _modService;
    private readonly LaunchService _launchService;
    private readonly GameUtilityService _gameUtilityService;
    private readonly UpdateService _updateService;
    private readonly SkinService _skinService;
    private readonly InstanceService _instanceService;
    private readonly UserIdentityService _userIdentityService;
    private readonly ProfileManagementService _profileManagementService;
    private readonly SettingsService _settingsService;
    private readonly FileDialogService _fileDialogService;
    private readonly LanguageService _languageService;
    private readonly AssetService _assetService;
    private readonly AvatarService _avatarService;
    private readonly RosettaService _rosettaService;
    private readonly BrowserService _browserService;
    private readonly ProgressNotificationService _progressNotificationService;
    private readonly LocalizationService _localizationService;
    
    // Exposed for ViewModel access
    public Config Configuration => _config;
    public ProfileService ProfileService => _profileService;
    public NewsService NewsService => _newsService;
    public VersionService VersionService => _versionService;
    public ModService ModService => _modService;
    public LocalizationService Localization => _localizationService;

    // UI Events (forwarded from ProgressNotificationService)
    public event Action<string, double, string, long, long>? DownloadProgressChanged
    {
        add => _progressNotificationService.DownloadProgressChanged += value;
        remove => _progressNotificationService.DownloadProgressChanged -= value;
    }
    public event Action<string, int>? GameStateChanged
    {
        add => _progressNotificationService.GameStateChanged += value;
        remove => _progressNotificationService.GameStateChanged -= value;
    }
    public event Action<string, string, string?>? ErrorOccurred
    {
        add => _progressNotificationService.ErrorOccurred += value;
        remove => _progressNotificationService.ErrorOccurred -= value;
    }
    public event Action<object>? LauncherUpdateAvailable;
    
    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);
    
    private static readonly HttpClient HttpClient = new()
    {
        // Use longer timeout for large file downloads - can be overridden per-request with cancellation tokens
        Timeout = TimeSpan.FromMinutes(30)
    };
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static AppService()
    {
        UtilityService.LoadEnvFile();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism/1.0");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public AppService()
    {
        _appDir = UtilityService.GetEffectiveAppDir();
        Directory.CreateDirectory(_appDir);
        _configPath = Path.Combine(_appDir, "config.json");
        _config = LoadConfig();
        
        // Initialize new services
        _configService = new ConfigService(_appDir);
        _config = _configService.Configuration; // Use config from ConfigService
        _profileService = new ProfileService(_appDir, _configService);
        _newsService = new NewsService();
        _versionService = new VersionService(_appDir, HttpClient, _configService);
        _downloadService = new DownloadService(HttpClient);
        _modService = new ModService(HttpClient, _appDir);
        _launchService = new LaunchService(_appDir, HttpClient);
        _instanceService = new InstanceService(
            _appDir,
            () => _config,
            SaveConfigInternal);
        _gameUtilityService = new GameUtilityService(
            _appDir,
            _config,
            UtilityService.NormalizeVersionType,
            ResolveInstancePath,
            branch => _instanceService.GetLatestInfoPath(branch),
            GetInstancePath,
            GetProfilesFolder,
            UtilityService.SanitizeFileName,
            () => _gameProcess,
            p => _gameProcess = p!);
        
        _updateService = new UpdateService(
            HttpClient,
            _config,
            _versionService,
            obj => LauncherUpdateAvailable?.Invoke(obj),
            BrowserOpenURL,
            UtilityService.NormalizeVersionType,
            branch => _instanceService.LoadLatestInfo(branch),
            (branch, version) => _instanceService.SaveLatestInfo(branch, version),
            () => GetLatestInstancePath(UtilityService.NormalizeVersionType(_config.VersionType)),
            path => _instanceService.IsClientPresent(path),
            (branch, version, createIfMissing) => ResolveInstancePath(branch, version, createIfMissing));
        _skinService = new SkinService(
            UtilityService.NormalizeVersionType,
            ResolveInstancePath,
            path => _instanceService.GetInstanceUserDataPath(path),
            () => _config,
            SaveConfig,
            GetProfilesFolder,
            UtilityService.SanitizeFileName);
        _userIdentityService = new UserIdentityService(
            () => _config,
            SaveConfig,
            _skinService,
            UtilityService.NormalizeVersionType,
            ResolveInstancePath,
            path => _instanceService.GetInstanceUserDataPath(path));
        _profileManagementService = new ProfileManagementService(
            _appDir,
            () => _config,
            SaveConfig,
            _skinService,
            UtilityService.NormalizeVersionType,
            ResolveInstancePath,
            path => _instanceService.GetInstanceUserDataPath(path),
            GetCurrentUuid);
        _settingsService = new SettingsService(
            () => _config,
            SaveConfig);
        _fileDialogService = new FileDialogService();
        _languageService = new LanguageService(
            async () => await GetVersionListAsync("release"),
            async () => await GetVersionListAsync("pre-release"),
            GetInstancePath,
            GetLatestInstancePath);
        _assetService = new AssetService(_instanceService, _appDir);
        _avatarService = new AvatarService(_instanceService, _appDir);
        _rosettaService = new RosettaService();
        _browserService = new BrowserService();
        
        // Initialize LocalizationService (reads from embedded resources)
        _localizationService = new LocalizationService();
        _localizationService.CurrentLanguage = _config.Language;
        
        // Initialize after DiscordService (needed below)
        _butlerService = new ButlerService(_appDir);
        _discordService = new DiscordService();
        _progressNotificationService = new ProgressNotificationService(_discordService);
        
        // Update placeholder names to random ones immediately
        if (_config.Nick == "Hyprism" || _config.Nick == "HyPrism" || _config.Nick == "Player")
        {
            _config.Nick = UtilityService.GenerateRandomUsername();
            SaveConfig();
            Logger.Info("Config", $"Updated placeholder username to: {_config.Nick}");
        }
        
        // IMPORTANT: Attempt to recover orphaned skin data after config is loaded.
        // This handles the case where config was reset but old skin files still exist.
        _skinService.TryRecoverOrphanedSkinOnStartup();
        
        _instanceService.MigrateLegacyData();
        _discordService.Initialize();
        
        // Initialize profile mods symlink if an active profile exists
        _profileManagementService.InitializeProfileModsSymlink();
    }

    /// <summary>
    /// Gets the effective app directory, checking for environment variable override first.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _skinService?.StopSkinProtection();
        _discordService?.Dispose();
    }
    
    /// <summary>
    /// Gets the UserData path for an instance. The game stores skins, settings, etc. here.
    /// </summary>
    private int ResolveVersionOrLatest(string branch, int version)
    {
        if (version > 0) return version;
        if (_config.SelectedVersion > 0) return _config.SelectedVersion;

        var info = _instanceService.LoadLatestInfo(branch);
        if (info?.Version > 0) return info.Version;

        string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? _config.VersionType : branch;
        string branchDir = _instanceService.GetBranchPath(resolvedBranch);
        if (Directory.Exists(branchDir))
        {
            var latest = Directory.GetDirectories(branchDir)
                .Select(Path.GetFileName)
                .Select(name => int.TryParse(name, out var v) ? v : -1)
                .Where(v => v > 0)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            return latest;
        }

        return 0;
    }

    private string GetLatestInstancePath(string branch) => _instanceService.GetLatestInstancePath(branch);

    private string GetInstancePath(string branch, int version) => _instanceService.GetInstancePath(branch, version);

    private string ResolveInstancePath(string branch, int version, bool preferExisting) => _instanceService.ResolveInstancePath(branch, version, preferExisting);

    private async Task<(string branch, int version)> ResolveLatestCompositeAsync()
    {
        var releaseVersions = await GetVersionListAsync("release");
        var preVersions = await GetVersionListAsync("pre-release");
        int releaseLatest = releaseVersions.FirstOrDefault();
        int preLatest = preVersions.FirstOrDefault();

        // If both missing, default to release 0
        if (releaseLatest == 0 && preLatest == 0)
        {
            return ("release", 0);
        }

        // Prefer whichever has the higher version number; tie goes to pre-release
        if (preLatest >= releaseLatest)
        {
            return ("pre-release", preLatest);
        }

        return ("release", releaseLatest);
    }

    private Config LoadConfig()
    {
        Config config;
        
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                
                // Migration: Ensure UUID exists
                if (string.IsNullOrEmpty(config.UUID))
                {
                    config.UUID = Guid.NewGuid().ToString();
                    config.Version = "2.0.0";
                    Logger.Info("Config", $"Migrated to v2.0.0, UUID: {config.UUID}");
                }
                
                // Migration: Migrate existing UUID to UserUuids mapping
                // This ensures existing users don't lose their skin when upgrading
                config.UserUuids ??= new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(config.UUID) && !string.IsNullOrEmpty(config.Nick))
                {
                    // Check if current nick already has a UUID mapping
                    var existingKey = config.UserUuids.Keys
                        .FirstOrDefault(k => k.Equals(config.Nick, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingKey == null)
                    {
                        // No mapping exists for current nick - add the legacy UUID
                        config.UserUuids[config.Nick] = config.UUID;
                        Logger.Info("Config", $"Migrated existing UUID to UserUuids mapping for '{config.Nick}'");
                        SaveConfigInternal(config);
                    }
                }
                
                return config;
            }
            catch
            {
                config = new Config();
            }
        }
        else
        {
            config = new Config();
        }
        
        // New config - generate UUID
        if (string.IsNullOrEmpty(config.UUID))
        {
            config.UUID = Guid.NewGuid().ToString();
        }

        // Default nick to random name if empty or placeholder
        if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player" || config.Nick == "Hyprism" || config.Nick == "HyPrism")
        {
            config.Nick = UtilityService.GenerateRandomUsername();
        }
        
        // Initialize UserUuids and add current user
        config.UserUuids ??= new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(config.Nick) && !string.IsNullOrEmpty(config.UUID))
        {
            config.UserUuids[config.Nick] = config.UUID;
        }

        // Migrate legacy "latest" branch to release
        if (config.VersionType == "latest")
        {
            config.VersionType = "release";
        }
        SaveConfigInternal(config);
        return config;
    }

    private void SaveConfigInternal(Config config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    public void SaveConfig()
    {
        SaveConfigInternal(_config);
    }
    
    public void SetLanguage(string languageCode)
    {
        if (LocalizationService.AvailableLanguages.ContainsKey(languageCode))
        {
            _config.Language = languageCode;
            _localizationService.CurrentLanguage = languageCode;
            SaveConfig();
            Logger.Info("Localization", $"Language changed to: {languageCode}");
        }
    }
    
    /// <summary>
    /// Attempts to recover orphaned skin data on startup.
    public Config QueryConfig() => _config;

    public string GetNick() => _profileService.GetNick();
    
    public string GetUUID() => _profileService.GetUUID();
    
    /// <summary>
    /// Gets the avatar preview image as base64 data URL for displaying in the launcher.
    /// Returns null if no avatar preview exists.
    /// </summary>
    public string? GetAvatarPreview() => _profileService.GetAvatarPreview();
    
    /// <summary>
    /// Gets the avatar preview for a specific UUID.
    /// Checks profile folder first, then game cache, then persistent backup.
    /// </summary>
    public string? GetAvatarPreviewForUUID(string uuid) => _profileService.GetAvatarPreviewForUUID(uuid);

    public string GetCustomInstanceDir() => _config.InstanceDirectory ?? "";

    public bool SetUUID(string uuid) => _profileService.SetUUID(uuid);
    
    /// <summary>
    /// Clears the avatar cache for the current UUID.
    /// Call this when the user wants to reset their avatar.
    /// </summary>
    public bool ClearAvatarCache()
    {
        var uuid = GetCurrentUuid();
        return _avatarService.ClearAvatarCache(uuid);
    }
    
    public bool SetNick(string nick) => _profileService.SetNick(nick);
    
    // ========== UUID Management (Username->UUID Mapping) ==========
    
    /// <summary>
    /// Gets or creates a UUID for a specific username.
    /// Uses case-insensitive lookup but preserves original username casing.
    /// This ensures each username consistently gets the same UUID across sessions.
    /// </summary>
    public string GetUuidForUser(string username) => _userIdentityService.GetUuidForUser(username);
    
    /// <summary>
    /// Gets the UUID for the current user (based on Nick).
    /// </summary>
    public string GetCurrentUuid() => _userIdentityService.GetCurrentUuid();
    
    /// <summary>
    /// Gets all username->UUID mappings.
    /// Returns a list of objects with username, uuid, and isCurrent properties.
    /// </summary>
    public List<UuidMapping> GetAllUuidMappings() => _userIdentityService.GetAllUuidMappings();
    
    /// <summary>
    /// Sets a custom UUID for a specific username.
    /// </summary>
    public bool SetUuidForUser(string username, string uuid) => _userIdentityService.SetUuidForUser(username, uuid);
    
    /// <summary>
    /// Deletes the UUID mapping for a specific username.
    /// Cannot delete the UUID for the current user.
    /// </summary>
    public bool DeleteUuidForUser(string username) => _userIdentityService.DeleteUuidForUser(username);
    
    /// <summary>
    /// Generates a new random UUID for the current user.
    /// Warning: This will change the player's identity and they will lose their skin!
    /// </summary>
    public string ResetCurrentUserUuid() => _userIdentityService.ResetCurrentUserUuid();
    
    /// <summary>
    /// Switches to an existing username (and its UUID).
    /// Returns the UUID for the username.
    /// </summary>
    public string? SwitchToUsername(string username) => _userIdentityService.SwitchToUsername(username);
    
    /// <summary>
    /// Attempts to recover orphaned skin data and associate it with the current user.
    /// This is useful when a user's config was reset but their skin data still exists.
    /// Returns true if skin data was recovered, false otherwise.
    /// </summary>
    public bool RecoverOrphanedSkinData() => _userIdentityService.RecoverOrphanedSkinData();
    
    /// <summary>
    /// Gets the UUID of any orphaned skin found in the game cache.
    /// Returns null if no orphaned skins are found.
    /// </summary>
    public string? GetOrphanedSkinUuid() => _userIdentityService.GetOrphanedSkinUuid();
    
    // ========== Profile Management ==========
    
    /// <summary>
    /// Gets all saved profiles, filtering out any with null/empty names.
    /// </summary>
    public List<Profile> GetProfiles() => _profileManagementService.GetProfiles();
    
    /// <summary>
    /// Gets the currently active profile index. -1 means no profile selected.
    /// </summary>
    public int GetActiveProfileIndex() => _profileManagementService.GetActiveProfileIndex();
    
    /// <summary>
    /// Creates a new profile with the given name and UUID.
    /// Returns the created profile.
    /// </summary>
    public Profile? CreateProfile(string name, string uuid) => _profileManagementService.CreateProfile(name, uuid);
    
    /// <summary>
    /// Deletes a profile by its ID.
    /// Returns true if successful.
    /// </summary>
    public bool DeleteProfile(string profileId) => _profileManagementService.DeleteProfile(profileId);
    
    /// <summary>
    /// Switches to a profile by its index.
    /// Returns true if successful.
    /// </summary>
    public bool SwitchProfile(int index) => _profileManagementService.SwitchProfile(index);
    
    /// <summary>
    /// Updates an existing profile.
    /// </summary>
    public bool UpdateProfile(string profileId, string? newName, string? newUuid) => 
        _profileManagementService.UpdateProfile(profileId, newName, newUuid);
    
    /// <summary>
    /// Gets the path to the Profiles folder.
    /// </summary>
    private string GetProfilesFolder() => _profileManagementService.GetProfilesFolder();
    
    /// <summary>
    /// Saves the current UUID/Nick as a new profile.
    /// Returns the created profile.
    /// </summary>
    public Profile? SaveCurrentAsProfile() => _profileManagementService.SaveCurrentAsProfile();
    
    /// <summary>
    /// Duplicates an existing profile (copies UserData folder too).
    /// Returns the newly created profile.
    /// </summary>
    public Profile? DuplicateProfile(string profileId) => _profileManagementService.DuplicateProfile(profileId);
    
    /// <summary>
    /// Duplicates an existing profile WITHOUT copying UserData folder.
    /// Only copies settings and configuration.
    /// Returns the newly created profile.
    /// </summary>
    public Profile? DuplicateProfileWithoutData(string profileId) => _profileManagementService.DuplicateProfileWithoutData(profileId);
    
    /// <summary>
    /// Opens the current active profile's folder in the file manager.
    /// </summary>
    public bool OpenCurrentProfileFolder() => _profileManagementService.OpenCurrentProfileFolder();

    public Task<string?> SetInstanceDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom instance directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _config.InstanceDirectory = null!;
                SaveConfig();
                Logger.Success("Config", "Instance directory cleared, using default");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(Path.Combine(_appDir, expanded));
            }

            Directory.CreateDirectory(expanded);

            _config.InstanceDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Instance directory set to {expanded}");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    public string GetLauncherVersion() => _updateService.GetLauncherVersion();

    /// <summary>
    /// Check if Rosetta 2 is installed on macOS Apple Silicon.
    /// Returns null if not on macOS or if Rosetta is installed.
    /// Returns a warning object if Rosetta is needed but not installed.
    /// </summary>
    public RosettaStatus? CheckRosettaStatus() => _rosettaService.CheckRosettaStatus();

    // Version Management
    public string GetVersionType() => _config.VersionType;
    
    public bool SetVersionType(string versionType)
    {
        _config.VersionType = UtilityService.NormalizeVersionType(versionType);
        SaveConfig();
        return true;
    }

    // Returns list of available version numbers by checking Hytale's patch server
    // Uses caching to start from the last known version instead of version 1
    public async Task<List<int>> GetVersionListAsync(string branch) => await _versionService.GetVersionListAsync(branch);

    public bool SetSelectedVersion(int versionNumber)
    {
        _config.SelectedVersion = versionNumber;
        SaveConfig();
        return true;
    }

    public bool IsVersionInstalled(string branch, int versionNumber)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);

        // Version 0 means "latest" - check if any version is installed
        if (versionNumber == 0)
        {
            var resolvedLatest = ResolveInstancePath(normalizedBranch, 0, preferExisting: true);
            bool hasClient = _instanceService.IsClientPresent(resolvedLatest);
            Logger.Info("Version", $"IsVersionInstalled check for version 0 (latest): path={resolvedLatest}, hasClient={hasClient}");
            return hasClient;
        }
        
        string versionPath = ResolveInstancePath(normalizedBranch, versionNumber, preferExisting: true);

        if (!_instanceService.IsClientPresent(versionPath))
        {
            // Last chance: try legacy dash naming in legacy roots
            var legacy = _instanceService.FindExistingInstancePath(normalizedBranch, versionNumber);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                Logger.Info("Version", $"IsVersionInstalled: found legacy layout at {legacy}");
                return _instanceService.IsClientPresent(legacy);
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if Assets.zip exists for the specified branch and version.
    /// Assets.zip is required for the skin customizer to work.
    /// </summary>
    public bool HasAssetsZip(string branch, int version)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);
        var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
        return _assetService.HasAssetsZip(versionPath);
    }
    
    /// <summary>
    /// Gets the path to Assets.zip if it exists, or null if not found.
    /// </summary>
    public string? GetAssetsZipPath(string branch, int version)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);
        var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
        return _assetService.GetAssetsZipPathIfExists(versionPath);
    }
    
    /// <summary>
    /// Gets the available cosmetics from the Assets.zip file for the specified instance.
    /// Returns a dictionary where keys are category names and values are lists of cosmetic IDs.
    /// </summary>
    public Dictionary<string, List<string>>? GetCosmeticsList(string branch, int version)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);
        var versionPath = ResolveInstancePath(normalizedBranch, version, preferExisting: true);
        return _assetService.GetCosmeticsList(versionPath);
    }

    public List<int> GetInstalledVersionsForBranch(string branch)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);
        var result = new HashSet<int>();

        foreach (var root in _instanceService.GetInstanceRootsIncludingLegacy())
        {
            // New layout: branch/version
            string branchPath = Path.Combine(root, normalizedBranch);
            if (Directory.Exists(branchPath))
            {
                foreach (var dir in Directory.GetDirectories(branchPath))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, "latest", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_instanceService.IsClientPresent(dir))
                        {
                            result.Add(0);
                            Logger.Info("Version", $"Installed versions include latest for {normalizedBranch} at {dir}");
                        }
                        continue;
                    }

                    if (int.TryParse(name, out int version))
                    {
                        if (_instanceService.IsClientPresent(dir))
                        {
                            result.Add(version);
                            Logger.Info("Version", $"Installed version detected: {normalizedBranch}/{version} at {dir}");
                        }
                    }
                }
            }

            // Legacy dash layout: release-29 or release-v29
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.StartsWith(normalizedBranch + "-", StringComparison.OrdinalIgnoreCase)) continue;

                var suffix = name.Substring(normalizedBranch.Length + 1);
                
                // Remove 'v' prefix if present (e.g., "v5" -> "5")
                if (suffix.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    suffix = suffix.Substring(1);
                }

                if (string.Equals(suffix, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    if (_instanceService.IsClientPresent(dir))
                    {
                        result.Add(0);
                        Logger.Info("Version", $"Installed legacy latest detected: {name} at {dir}");
                    }
                    continue;
                }

                if (int.TryParse(suffix, out int version))
                {
                    if (_instanceService.IsClientPresent(dir))
                    {
                        result.Add(version);
                        Logger.Info("Version", $"Installed legacy version detected: {name} at {dir}");
                    }
                }
            }
        }
        
        return result.ToList();
    }

    public async Task<bool> CheckLatestNeedsUpdateAsync(string branch)
    {
        var normalizedBranch = UtilityService.NormalizeVersionType(branch);
        var versions = await GetVersionListAsync(normalizedBranch);
        if (versions.Count == 0) return false;

        var latest = versions[0];
        var latestPath = GetLatestInstancePath(normalizedBranch);
        var info = _instanceService.LoadLatestInfo(normalizedBranch);
        var baseOk = _instanceService.IsClientPresent(latestPath);
        if (!baseOk) return true;
        if (info == null)
        {
            // Game is installed but no version tracking - assume needs update to be safe
            // Don't write anything here - let the user decide via UPDATE button
            Logger.Info("Update", $"No latest.json found for {normalizedBranch}, assuming update may be needed");
            return true;
        }
        return info.Version != latest;
    }
    
    /// <summary>
    /// Gets the version status for the latest instance.
    /// </summary>
    public async Task<VersionStatus> GetLatestVersionStatusAsync(string branch)
    {
        return await _versionService.GetLatestVersionStatusAsync(
            branch,
            _instanceService.IsClientPresent,
            () => GetLatestInstancePath(UtilityService.NormalizeVersionType(branch)),
            (b) => {
                var info = _instanceService.LoadLatestInfo(b);
                return info != null ? new LatestVersionInfo { Version = info.Version } : null;
            }
        );
    }
    
    /// <summary>
    /// Forces the latest instance to update by resetting its version info.
    /// This will trigger a differential update on next launch.
    /// </summary>
    public async Task<bool> ForceUpdateLatestAsync(string branch) => 
        await _updateService.ForceUpdateLatestAsync(branch);
    
    /// <summary>
    /// Duplicates the current latest instance as a versioned instance.
    /// </summary>
    public async Task<bool> DuplicateLatestAsync(string branch) => 
        await _updateService.DuplicateLatestAsync(branch);

    /// <summary>
    /// Get information about the pending update, including old version details.
    /// Returns null if no update is pending.
    /// </summary>
    public async Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return null;

            var latestVersion = versions[0];
            var latestPath = GetLatestInstancePath(normalizedBranch);
            var info = _instanceService.LoadLatestInfo(normalizedBranch);
            
            // Check if update is needed
            if (info == null || info.Version == latestVersion) return null;
            
            // Check if old version has userdata
            var oldUserDataPath = Path.Combine(latestPath, "UserData");
            var hasOldUserData = Directory.Exists(oldUserDataPath) && 
                                 Directory.GetFileSystemEntries(oldUserDataPath).Length > 0;
            
            return new UpdateInfo
            {
                OldVersion = info.Version,
                NewVersion = latestVersion,
                HasOldUserData = hasOldUserData,
                Branch = normalizedBranch
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Failed to get pending update info: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copy userdata from one version to another.
    /// </summary>
    public async Task<bool> CopyUserDataAsync(string branch, int fromVersion, int toVersion)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            
            // Get source path (if fromVersion is 0, use latest)
            string fromPath;
            if (fromVersion == 0)
            {
                fromPath = GetLatestInstancePath(normalizedBranch);
            }
            else
            {
                fromPath = ResolveInstancePath(normalizedBranch, fromVersion, preferExisting: true);
            }
            
            // Get destination path (if toVersion is 0, use latest)
            string toPath;
            if (toVersion == 0)
            {
                toPath = GetLatestInstancePath(normalizedBranch);
            }
            else
            {
                toPath = ResolveInstancePath(normalizedBranch, toVersion, preferExisting: true);
            }
            
            var fromUserData = Path.Combine(fromPath, "UserData");
            var toUserData = Path.Combine(toPath, "UserData");
            
            if (!Directory.Exists(fromUserData))
            {
                Logger.Warning("UserData", $"Source UserData does not exist: {fromUserData}");
                return false;
            }
            
            // Create destination if needed
            Directory.CreateDirectory(toUserData);
            
            // Copy all contents
            await Task.Run(() => UtilityService.CopyDirectory(fromUserData, toUserData, true));
            
            Logger.Success("UserData", $"Copied UserData from v{fromVersion} to v{toVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UserData", $"Failed to copy userdata: {ex.Message}");
            return false;
        }
    }

    // Game
    public async Task<DownloadProgress> DownloadAndLaunchAsync()
    {
        try
        {
            _downloadCts = new CancellationTokenSource();
            
            string branch = UtilityService.NormalizeVersionType(_config.VersionType);
            var versions = await GetVersionListAsync(branch);
            if (versions.Count == 0)
            {
                return new DownloadProgress { Error = "No versions available for this branch" };
            }

            bool isLatestInstance = _config.SelectedVersion == 0;
            int targetVersion = _config.SelectedVersion > 0 ? _config.SelectedVersion : versions[0];
            if (!versions.Contains(targetVersion))
            {
                targetVersion = versions[0];
            }

            string versionPath = ResolveInstancePath(branch, isLatestInstance ? 0 : targetVersion, preferExisting: true);
            Directory.CreateDirectory(versionPath);

            // Check if we need to download/install - verify all components
            // The game is installed if the Client executable exists - that's all we need to check
            bool gameIsInstalled = _instanceService.IsClientPresent(versionPath);
            
            Logger.Info("Download", $"=== INSTALL CHECK ===");
            Logger.Info("Download", $"Version path: {versionPath}");
            Logger.Info("Download", $"Is latest instance: {isLatestInstance}");
            Logger.Info("Download", $"Target version: {targetVersion}");
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}");
            
            // If game is already installed, check for updates then launch
            if (gameIsInstalled)
            {
                Logger.Success("Download", "Game is already installed");
                
                // Check if we need a differential update (only for latest instance)
                if (isLatestInstance)
                {
                    var info = _instanceService.LoadLatestInfo(branch);
                    int installedVersion = info?.Version ?? 0;
                    int latestVersion = versions[0];
                    
                    // If no latest.json exists, we need to determine the installed version
                    if (installedVersion == 0)
                    {
                        // First, check if there's a Butler receipt which indicates the game was installed via Butler
                        var receiptPath = Path.Combine(versionPath, ".itch", "receipt.json.gz");
                        bool hasButlerReceipt = File.Exists(receiptPath);
                        
                        if (hasButlerReceipt)
                        {
                            // Butler receipt exists - the game was installed/patched by Butler
                            // Check if we have any cached PWR files that indicate a version
                            var cacheDir = Path.Combine(_appDir, "cache");
                            if (Directory.Exists(cacheDir))
                            {
                                var pwrFiles = Directory.GetFiles(cacheDir, $"{branch}_patch_*.pwr")
                                    .Concat(Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
                                    .Select(f => Path.GetFileNameWithoutExtension(f))
                                    .SelectMany(n => {
                                        // Try to extract version from filename patterns like "release_patch_7" or "release_7"
                                        var parts = n.Split('_');
                                        var versions = new List<int>();
                                        foreach (var part in parts)
                                        {
                                            if (int.TryParse(part, out var v) && v > 0)
                                            {
                                                versions.Add(v);
                                            }
                                        }
                                        return versions;
                                    })
                                    .OrderByDescending(v => v)
                                    .ToList();
                                
                                if (pwrFiles.Count > 0)
                                {
                                    // The highest version in cache is likely the installed version
                                    installedVersion = pwrFiles[0];
                                    Logger.Info("Download", $"Detected installed version from cache: v{installedVersion}");
                                    // Save the detected version
                                    _instanceService.SaveLatestInfo(branch, installedVersion);
                                }
                            }
                            
                            // If still no version detected but receipt exists, don't assume anything
                            // User can click UPDATE button if they want to ensure they're on latest
                            if (installedVersion == 0)
                            {
                                // Game has Butler receipt but no version info - don't assume version
                                // Just launch as-is, user can click UPDATE if needed
                                Logger.Info("Download", $"Butler receipt exists but no version info, launching as-is (user can UPDATE manually)");
                            }
                        }
                        else
                        {
                            // No Butler receipt - this is a legacy installation or was installed differently
                            // Don't assume version, just launch as-is
                            Logger.Info("Download", $"No Butler receipt, launching current installation as-is (user can UPDATE manually)");
                        }
                        
                        // Only save if we actually detected a version from cache
                        // Don't assume latest - that breaks update detection
                    }
                    
                    Logger.Info("Download", $"Installed version: {installedVersion}, Latest version: {latestVersion}");
                    
                    // Only apply differential update if we're BEHIND the latest version
                    if (installedVersion > 0 && installedVersion < latestVersion)
                    {
                        Logger.Info("Download", $"Differential update available: {installedVersion} -> {latestVersion}");
                        SendProgress("update", 0, $"Updating game from v{installedVersion} to v{latestVersion}...", 0, 0);
                        
                        try
                        {
                            // Apply differential updates for each version step
                            var patchesToApply = _versionService.GetPatchSequence(installedVersion, latestVersion);
                            Logger.Info("Download", $"Patches to apply: {string.Join(" -> ", patchesToApply)}");
                            
                            for (int i = 0; i < patchesToApply.Count; i++)
                            {
                                int patchVersion = patchesToApply[i];
                                ThrowIfCancelled();
                                
                                // Progress: each patch gets an equal share of 0-90%
                                int baseProgress = (i * 90) / patchesToApply.Count;
                                int progressPerPatch = 90 / patchesToApply.Count;
                                
                                SendProgress("update", baseProgress, $"Downloading patch {i + 1}/{patchesToApply.Count} (v{patchVersion})...", 0, 0);
                                
                                // Ensure Butler is installed
                                await _butlerService.EnsureButlerInstalledAsync((p, m) => { });
                                
                                // Download the PWR patch
                                var patchOs = UtilityService.GetOS();
                                var patchArch = UtilityService.GetArch();
                                var patchBranchType = UtilityService.NormalizeVersionType(branch);
                                string patchUrl = $"https://game-patches.hytale.com/patches/{patchOs}/{patchArch}/{patchBranchType}/0/{patchVersion}.pwr";
                                string patchPwrPath = Path.Combine(_appDir, "cache", $"{branch}_patch_{patchVersion}.pwr");
                                
                                Directory.CreateDirectory(Path.GetDirectoryName(patchPwrPath)!);
                                Logger.Info("Download", $"Downloading patch: {patchUrl}");
                                
                                // Check if patch file is very large (> 500MB) - might indicate wrong version detection
                                // In that case, we should fall back to the existing installation
                                try
                                {
                                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, patchUrl);
                                    using var headResponse = await HttpClient.SendAsync(headRequest);
                                    
                                    if (!headResponse.IsSuccessStatusCode)
                                    {
                                        Logger.Warning("Download", $"Patch file not found at {patchUrl}, skipping differential update");
                                        throw new Exception("Patch file not available");
                                    }
                                    
                                    var contentLength = headResponse.Content.Headers.ContentLength ?? 0;
                                    Logger.Info("Download", $"Patch file size: {contentLength / 1024 / 1024} MB");
                                    
                                    // If patch is > 500MB, something is wrong - patches should be small
                                    if (contentLength > 500 * 1024 * 1024)
                                    {
                                        Logger.Warning("Download", $"Patch file is too large ({contentLength / 1024 / 1024} MB), likely wrong version detection");
                                        throw new Exception("Patch file unexpectedly large - version detection may be incorrect");
                                    }
                                }
                                catch (HttpRequestException)
                                {
                                    Logger.Warning("Download", $"Cannot check patch file at {patchUrl}, skipping differential update");
                                    throw new Exception("Cannot access patch file");
                                }
                                
                                await DownloadFileAsync(patchUrl, patchPwrPath, (progress, downloaded, total) =>
                                {
                                    int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    SendProgress("update", mappedProgress, $"Downloading patch {i + 1}/{patchesToApply.Count}... {progress}%", downloaded, total);
                                }, _downloadCts.Token);
                                
                                ThrowIfCancelled();
                                
                                // Apply the patch using Butler (differential update)
                                int applyBaseProgress = baseProgress + (progressPerPatch / 2);
                                SendProgress("update", applyBaseProgress, $"Applying patch {i + 1}/{patchesToApply.Count}...", 0, 0);
                                
                                await _butlerService.ApplyPwrAsync(patchPwrPath, versionPath, (progress, message) =>
                                {
                                    int mappedProgress = applyBaseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                                    SendProgress("update", mappedProgress, message, 0, 0);
                                }, _downloadCts.Token);
                                
                                // Clean up patch file
                                if (File.Exists(patchPwrPath))
                                {
                                    try { File.Delete(patchPwrPath); } catch { }
                                }
                                
                                // Save progress after each patch
                                _instanceService.SaveLatestInfo(branch, patchVersion);
                                Logger.Success("Download", $"Patch {patchVersion} applied successfully");
                            }
                            
                            Logger.Success("Download", $"Differential update complete: now at v{latestVersion}");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Download", $"Differential update failed: {ex.Message}");
                            // Don't update latest.json - keep the old version so user can try UPDATE again
                            // Just launch the game as-is with whatever version is installed
                            Logger.Warning("Download", "Keeping current version, user can try UPDATE again later");
                        }
                    }
                    else if (installedVersion >= latestVersion)
                    {
                        Logger.Info("Download", "Already at latest version, no update needed");
                        // Ensure latest.json is correct
                        _instanceService.SaveLatestInfo(branch, latestVersion);
                    }
                }
                
                // Ensure VC++ Redistributable is installed on Windows before launching
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SendProgress("install", 94, "Checking Visual C++ Runtime...", 0, 0);
                    try
                    {
                        await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 94 + (int)(progress * 0.02);
                            SendProgress("install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                        // Don't fail - continue anyway
                    }
                }
                
                // Just ensure JRE is available (download if needed, but don't touch the game)
                string jrePath = _launchService.GetJavaPath();
                if (!File.Exists(jrePath))
                {
                    Logger.Info("Download", "JRE missing, installing...");
                    SendProgress("install", 96, "Installing Java Runtime...", 0, 0);
                    try
                    {
                        await _launchService.EnsureJREInstalledAsync((progress, message) =>
                        {
                            int mappedProgress = 96 + (int)(progress * 0.03);
                            SendProgress("install", mappedProgress, message, 0, 0);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                        return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
                    }
                }
                
                SendProgress("complete", 100, "Launching game...", 0, 0);
                try
                {
                    await LaunchGameAsync(versionPath, branch);
                    return new DownloadProgress { Success = true, Progress = 100 };
                }
                catch (Exception ex)
                {
                    Logger.Error("Game", $"Launch failed: {ex.Message}");
                    SendErrorEvent("launch", "Failed to launch game", ex.ToString());
                    return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
                }
            }
            
            // Game is NOT installed - need to download
            Logger.Info("Download", "Game not installed, starting download...");

            SendProgress("download", 0, "Preparing download...", 0, 0);
            
            // First, ensure Butler is installed (0-5% progress)
            try
            {
                await _butlerService.EnsureButlerInstalledAsync((progress, message) =>
                {
                    // Map butler install progress to 0-5%
                    int mappedProgress = (int)(progress * 0.05);
                    SendProgress("download", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Butler install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
            }

            ThrowIfCancelled();
            
            // Download PWR file (5-70% progress)
            string osName = UtilityService.GetOS();
            string arch = UtilityService.GetArch();
            string apiVersionType = UtilityService.NormalizeVersionType(branch);
            string downloadUrl = $"https://game-patches.hytale.com/patches/{osName}/{arch}/{apiVersionType}/0/{targetVersion}.pwr";
            string pwrPath = Path.Combine(_appDir, "cache", $"{branch}_{(isLatestInstance ? "latest" : "version")}_{targetVersion}.pwr");
            
            Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);
            
            Logger.Info("Download", $"Downloading: {downloadUrl}");
            
            await DownloadFileAsync(downloadUrl, pwrPath, (progress, downloaded, total) =>
            {
                // Map download progress to 5-65%
                int mappedProgress = 5 + (int)(progress * 0.60);
                SendProgress("download", mappedProgress, $"Downloading... {progress}%", downloaded, total);
            }, _downloadCts.Token);
            
            // Extract PWR file using Butler (65-85% progress)
            SendProgress("install", 65, "Installing game with Butler...", 0, 0);
            
            try
            {
                await _butlerService.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
                {
                    // Map install progress (0-100) to 65-85%
                    int mappedProgress = 65 + (int)(progress * 0.20);
                    SendProgress("install", mappedProgress, message, 0, 0);
                }, _downloadCts.Token);
                
                // Clean up PWR file after successful extraction
                if (File.Exists(pwrPath))
                {
                    try { File.Delete(pwrPath); } catch { }
                }
                
                // Skip assets extraction on install to match legacy layout
                ThrowIfCancelled();
            }
            catch (OperationCanceledException)
            {
                // Re-throw cancellation to be handled by outer catch
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
            }

            if (isLatestInstance)
            {
                _instanceService.SaveLatestInfo(branch, targetVersion);
            }
            
            SendProgress("complete", 95, "Download complete!", 0, 0);

            // Ensure VC++ Redistributable is installed on Windows before launching
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SendProgress("install", 95, "Checking Visual C++ Runtime...", 0, 0);
                try
                {
                    await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                    {
                        int mappedProgress = 95 + (int)(progress * 0.01);
                        SendProgress("install", mappedProgress, message, 0, 0);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
                    // Don't fail - continue anyway
                }
            }

            // Ensure JRE is installed before launching
            SendProgress("install", 96, "Checking Java Runtime...", 0, 0);
            try
            {
                await _launchService.EnsureJREInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 96 + (int)(progress * 0.03); // 96-99%
                    SendProgress("install", mappedProgress, message, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("JRE", $"JRE install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install Java Runtime: {ex.Message}" };
            }

            ThrowIfCancelled();

            SendProgress("complete", 100, "Launching game...", 0, 0);

            // Launch the game
            try
            {
                await LaunchGameAsync(versionPath, branch);
                return new DownloadProgress { Success = true, Progress = 100 };
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Launch failed: {ex.Message}");
                SendErrorEvent("launch", "Failed to launch game", ex.ToString());
                return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Download cancelled");
            try
            {
                SendProgress("cancelled", 0, "Cancelled", 0, 0);
            }
            catch { }
            return new DownloadProgress { Error = "Download cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Error: {ex.Message}");
            return new DownloadProgress { Error = ex.Message };
        }
        finally
        {
            _downloadCts = null;
        }
    }

    public bool CancelDownload()
    {
        Logger.Info("Download", "CancelDownload called");
        if (_downloadCts != null)
        {
            Logger.Info("Download", "Cancelling download...");
            _downloadCts.Cancel();
            Logger.Info("Download", "Download cancellation requested");
            return true;
        }
        Logger.Warning("Download", "No download in progress to cancel");
        return false;
    }

    private void ThrowIfCancelled()
    {
        if (_downloadCts?.IsCancellationRequested == true)
        {
            throw new OperationCanceledException();
        }
    }

    private void SendProgress(string stage, int progress, string message, long downloaded, long total)
    {
        _progressNotificationService.SendProgress(stage, progress, message, downloaded, total);
    }

    private async Task DownloadFileAsync(string url, string path, Action<int, long, long> progressCallback, CancellationToken cancellationToken = default)
    {
        await _downloadService.DownloadFileAsync(url, path, progressCallback, cancellationToken);
    }

    private async Task LaunchGameAsync(string versionPath, string branch)
    {
        Logger.Info("Game", $"Preparing to launch from {versionPath}");
        
        string executable;
        string workingDir;
        string gameDir = versionPath;
        
        // Determine client path based on OS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            executable = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            // Set working directory to the MacOS folder where the executable is located
            workingDir = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = Path.Combine(versionPath, "Client", "HytaleClient.exe");
            workingDir = Path.Combine(versionPath, "Client");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }
        else
        {
            // Linux
            executable = Path.Combine(versionPath, "Client", "HytaleClient");
            workingDir = Path.Combine(versionPath, "Client");
            
            if (!File.Exists(executable))
            {
                Logger.Error("Game", $"Game client not found at {executable}");
                throw new Exception($"Game client not found at {executable}");
            }
        }

        // On macOS, clear quarantine attributes BEFORE patching
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
            UtilityService.ClearMacQuarantine(appBundle);
            Logger.Info("Game", "Cleared macOS quarantine attributes before patching");
        }

        // Patch binary to accept custom auth server tokens
        // The auth domain is "sessions.sanasol.ws" but we need to patch "hytale.com" -> "sanasol.ws"
        // so that sessions.hytale.com becomes sessions.sanasol.ws
        // NOTE: Patching is needed even in offline/insecure mode because the game still validates domains
        bool enablePatching = true;
        if (enablePatching && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            try
            {
                // Extract the base domain from auth domain (e.g., "sessions.sanasol.ws" -> "sanasol.ws")
                string baseDomain = _config.AuthDomain;
                if (baseDomain.StartsWith("sessions."))
                {
                    baseDomain = baseDomain.Substring("sessions.".Length);
                }
                
                Logger.Info("Game", $"Patching binary: hytale.com -> {baseDomain}");
                var patcher = new ClientPatcher(baseDomain);
                
                // Patch client binary first
                var patchResult = patcher.EnsureClientPatched(versionPath, (msg, progress) =>
                {
                    if (progress.HasValue)
                    {
                        Logger.Info("Patcher", $"{msg} ({progress}%)");
                    }
                    else
                    {
                        Logger.Info("Patcher", msg);
                    }
                });
                
                // Also patch server JAR (required for singleplayer to work)
                Logger.Info("Game", $"Patching server JAR: sessions.hytale.com -> sessions.{baseDomain}");
                var serverPatchResult = patcher.PatchServerJar(versionPath, (msg, progress) =>
                {
                    if (progress.HasValue)
                    {
                        Logger.Info("Patcher", $"{msg} ({progress}%)");
                    }
                    else
                    {
                        Logger.Info("Patcher", msg);
                    }
                });
                
                if (patchResult.Success)
                {
                    if (patchResult.AlreadyPatched)
                    {
                        Logger.Info("Game", "Client binary already patched");
                    }
                    else if (patchResult.PatchCount > 0)
                    {
                        Logger.Success("Game", $"Client binary patched successfully ({patchResult.PatchCount} occurrences)");
                        
                        // Re-sign the binary after patching (macOS requirement)
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            try
                            {
                                Logger.Info("Game", "Re-signing patched binary...");
                                string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
                                bool signed = ClientPatcher.SignMacOSBinary(appBundle);
                                if (signed)
                                {
                                    Logger.Success("Game", "Binary re-signed successfully");
                                }
                                else
                                {
                                    Logger.Warning("Game", "Binary signing failed - game may not launch");
                                }
                            }
                            catch (Exception signEx)
                            {
                                Logger.Warning("Game", $"Error re-signing binary: {signEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Info("Game", "No client patches needed - binary uses unknown encoding or already patched");
                    }
                }
                else
                {
                    Logger.Warning("Game", $"Client binary patching failed: {patchResult.Error}");
                    Logger.Info("Game", "Continuing launch anyway - may not connect to custom auth server");
                }
                
                // Log server JAR patch result
                if (serverPatchResult.Success)
                {
                    if (serverPatchResult.AlreadyPatched)
                    {
                        Logger.Info("Game", "Server JAR already patched");
                    }
                    else if (serverPatchResult.PatchCount > 0)
                    {
                        Logger.Success("Game", $"Server JAR patched successfully ({serverPatchResult.PatchCount} occurrences)");
                    }
                    else if (!string.IsNullOrEmpty(serverPatchResult.Warning))
                    {
                        Logger.Info("Game", $"Server JAR: {serverPatchResult.Warning}");
                    }
                }
                else
                {
                    Logger.Warning("Game", $"Server JAR patching failed: {serverPatchResult.Error}");
                    Logger.Info("Game", "Singleplayer may not work properly");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Game", $"Error during binary patching: {ex.Message}");
                Logger.Info("Game", "Continuing launch anyway - may not connect to custom auth server");
            }
        }

        // STEP 1: Determine UUID to use for this session
        // Use the username->UUID mapping to ensure consistent UUIDs across sessions
        // This is the key fix for skin persistence - each username always gets the same UUID
        string sessionUuid = GetUuidForUser(_config.Nick);
        Logger.Info("Game", $"Using UUID for user '{_config.Nick}': {sessionUuid}");

        // STEP 2: Fetch auth token - only if OnlineMode is enabled
        // If user wants offline mode, skip token fetching entirely
        string? identityToken = null;
        string? sessionToken = null;
        
        if (_config.OnlineMode && !string.IsNullOrWhiteSpace(_config.AuthDomain))
        {
            Logger.Info("Game", $"Online mode enabled - fetching auth tokens from {_config.AuthDomain}...");
            
            try
            {
                var authService = new AuthService(HttpClient, _config.AuthDomain);
                
                // Use sessionUuid (which may be fresh/random) for auth
                var tokenResult = await authService.GetGameSessionTokenAsync(sessionUuid, _config.Nick);
                
                if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Token))
                {
                    identityToken = tokenResult.Token;
                    sessionToken = tokenResult.SessionToken ?? tokenResult.Token; // Fallback to identity token
                    Logger.Success("Game", "Identity token obtained successfully");
                    Logger.Success("Game", "Session token obtained successfully");
                }
                else
                {
                    Logger.Warning("Game", $"Could not get auth token: {tokenResult.Error}");
                    Logger.Info("Game", "Will try launching with offline mode instead");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Game", $"Error fetching auth token: {ex.Message}");
                Logger.Info("Game", "Will try launching with offline mode instead");
            }
        }

        // Get Java path
        string javaPath = _launchService.GetJavaPath();
        if (!File.Exists(javaPath))
        {
            Logger.Error("Game", $"Java not found at {javaPath}");
            throw new Exception($"Java not found at {javaPath}");
        }
        
        // Verify Java is executable by running --version
        try
        {
            var javaCheck = new ProcessStartInfo(javaPath, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var javaProcess = Process.Start(javaCheck);
            if (javaProcess != null)
            {
                string javaOutput = await javaProcess.StandardOutput.ReadToEndAsync();
                await javaProcess.WaitForExitAsync();
                if (javaProcess.ExitCode == 0)
                {
                    Logger.Success("Game", $"Java verified: {javaOutput.Split('\n')[0]}");
                }
                else
                {
                    Logger.Warning("Game", $"Java check returned exit code {javaProcess.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Could not verify Java: {ex.Message}");
        }

        // Use per-instance UserData folder - this keeps skins/settings with the game instance
        string userDataDir = _instanceService.GetInstanceUserDataPath(versionPath);
        Directory.CreateDirectory(userDataDir);
        
        // Restore current profile's skin data before launching the game
        // This ensures the player's custom skin is loaded from their profile
        var currentProfile = _config.Profiles?.FirstOrDefault(p => p.UUID == sessionUuid);
        string? skinCachePath = null;
        if (currentProfile != null)
        {
            _skinService.RestoreProfileSkinData(currentProfile);
            Logger.Info("Game", $"Restored skin data for profile '{currentProfile.Name}'");
            
            // Start skin protection - this watches the skin file and restores it if the game overwrites it
            // The game may fetch skin data from the server on startup which could overwrite our local cache
            skinCachePath = Path.Combine(userDataDir, "CachedPlayerSkins", $"{currentProfile.UUID}.json");
            if (File.Exists(skinCachePath))
            {
                _skinService.StartSkinProtection(currentProfile, skinCachePath);
            }
        }

        Logger.Info("Game", $"Launching: {executable}");
        Logger.Info("Game", $"Java: {javaPath}");
        Logger.Info("Game", $"AppDir: {gameDir}");
        Logger.Info("Game", $"UserData: {userDataDir}");
        Logger.Info("Game", $"Online Mode: {_config.OnlineMode}");
        Logger.Info("Game", $"Session UUID: {sessionUuid}");

        // On macOS/Linux, create a launch script to run with clean environment
        ProcessStartInfo startInfo;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Use ArgumentList for proper escaping
            startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            
            // Add arguments using ArgumentList for proper Windows escaping
            startInfo.ArgumentList.Add("--app-dir");
            startInfo.ArgumentList.Add(gameDir);
            startInfo.ArgumentList.Add("--user-dir");
            startInfo.ArgumentList.Add(userDataDir);
            startInfo.ArgumentList.Add("--java-exec");
            startInfo.ArgumentList.Add(javaPath);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(_config.Nick);
            
            // Add auth mode based on user's OnlineMode preference
            // If OnlineMode is OFF, always use offline mode regardless of tokens
            // If OnlineMode is ON and we have tokens, use authenticated mode
            if (_config.OnlineMode && !string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
            {
                startInfo.ArgumentList.Add("--auth-mode");
                startInfo.ArgumentList.Add("authenticated");
                startInfo.ArgumentList.Add("--uuid");
                startInfo.ArgumentList.Add(sessionUuid);
                startInfo.ArgumentList.Add("--identity-token");
                startInfo.ArgumentList.Add(identityToken);
                startInfo.ArgumentList.Add("--session-token");
                startInfo.ArgumentList.Add(sessionToken);
                Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
            }
            else
            {
                // Offline mode - either user selected it or no tokens available
                startInfo.ArgumentList.Add("--auth-mode");
                startInfo.ArgumentList.Add("offline");
                startInfo.ArgumentList.Add("--uuid");
                startInfo.ArgumentList.Add(sessionUuid);
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            // Log the arguments for debugging
            Logger.Info("Game", $"Windows launch args: {string.Join(" ", startInfo.ArgumentList)}");
        }
        else
        {
            // Build arguments for the launch script - use only documented game arguments
            var gameArgs = new List<string>
            {
                $"--app-dir \"{gameDir}\"",
                $"--user-dir \"{userDataDir}\"",
                $"--java-exec \"{javaPath}\"",
                $"--name \"{_config.Nick}\""
            };
            
            // Add auth mode based on user's OnlineMode preference
            if (_config.OnlineMode && !string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
            {
                gameArgs.Add("--auth-mode authenticated");
                gameArgs.Add($"--uuid \"{sessionUuid}\"");
                gameArgs.Add($"--identity-token \"{identityToken}\"");
                gameArgs.Add($"--session-token \"{sessionToken}\"");
                Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
            }
            else
            {
                // Offline mode - either user selected it or no tokens available
                gameArgs.Add("--auth-mode offline");
                gameArgs.Add($"--uuid \"{sessionUuid}\"");
                Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
            }
            
            // macOS/Linux: Use env to run with completely clean environment
            // This prevents .NET runtime environment variables from interfering
            string argsString = string.Join(" ", gameArgs);
            string launchScript = Path.Combine(versionPath, "launch.sh");
            
            string homeDir = Environment.GetEnvironmentVariable("HOME") ?? "/Users/" + Environment.UserName;
            string userName = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
            
            // Get the Client directory for LD_LIBRARY_PATH (needed for shared libraries like SDL3_image.so)
            string clientDir = Path.Combine(versionPath, "Client");
            
            // Write the launch script with env to start with empty environment
            string scriptContent = $@"#!/bin/bash
# Launch script generated by HyPrism
# Uses env to clear ALL environment variables before launching game

# Set LD_LIBRARY_PATH to include Client directory for shared libraries (SDL3_image.so, etc.)
CLIENT_DIR=""{clientDir}""

exec env \
    HOME=""{homeDir}"" \
    USER=""{userName}"" \
    PATH=""/usr/bin:/bin:/usr/sbin:/sbin:/usr/local/bin"" \
    SHELL=""/bin/zsh"" \
    TMPDIR=""{Path.GetTempPath().TrimEnd('/')}"" \
    LD_LIBRARY_PATH=""$CLIENT_DIR:$LD_LIBRARY_PATH"" \
    ""{executable}"" {argsString}
";
            File.WriteAllText(launchScript, scriptContent);
            
            // Make it executable
            var chmod = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{launchScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
            
            // Use /bin/bash to run the script
            startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            startInfo.ArgumentList.Add(launchScript);
            
            Logger.Info("Game", $"Launch script: {launchScript}");
        }
        
        try
        {
            _gameProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Failed to start game process: {ex.Message}");
            SendErrorEvent("launch", "Failed to start game", ex.Message);
            throw new Exception($"Failed to start game: {ex.Message}");
        }
        
        if (_gameProcess == null)
        {
            Logger.Error("Game", "Process.Start returned null - game failed to launch");
            SendErrorEvent("launch", "Failed to start game", "Process.Start returned null");
            throw new Exception("Failed to start game process");
        }
        
        Logger.Success("Game", $"Game started with PID: {_gameProcess.Id}");
        
        // Set Discord presence to Playing
        _discordService.SetPresence(DiscordService.PresenceState.Playing, $"Playing as {_config.Nick}");
        
        // Notify frontend that game has launched
        SendGameStateEvent("started");
        
        // Handle process exit in background
        _ = Task.Run(async () =>
        {
            await _gameProcess.WaitForExitAsync();
            var exitCode = _gameProcess.ExitCode;
            Logger.Info("Game", $"Game process exited with code: {exitCode}");
            _gameProcess = null;
            
            // Stop skin protection first - allow normal skin file operations now
            _skinService.StopSkinProtection();
            
            // Backup current profile's skin data after game exits (save any changes made during gameplay)
            _skinService.BackupProfileSkinData(GetCurrentUuid());
            
            // Set Discord presence back to Idle
            _discordService.SetPresence(DiscordService.PresenceState.Idle);
            
            // Notify frontend that game has exited with exit code
            SendGameStateEvent("stopped", exitCode);
        });
    }

    private void SendGameStateEvent(string state, int? exitCode = null)
    {
        _progressNotificationService.SendGameStateEvent(state, exitCode);
    }

    private void SendErrorEvent(string type, string message, string? technical = null)
    {
        _progressNotificationService.SendErrorEvent(type, message, technical);
    }

    // Check for launcher updates and emit event if available
    public async Task CheckForLauncherUpdatesAsync() => 
        await _updateService.CheckForLauncherUpdatesAsync();

    public bool IsGameRunning() => _gameUtilityService.IsGameRunning();

    public List<string> GetRecentLogs(int count = 10)
    {
        return Logger.GetRecentLogs(count);
    }

    public bool ExitGame() => _gameUtilityService.ExitGame();

    public bool DeleteGame(string branch, int versionNumber) => _gameUtilityService.DeleteGame(branch, versionNumber);

    // Folder
    public bool OpenFolder() => _gameUtilityService.OpenFolder();

    public Task<string?> SelectInstanceDirectoryAsync()
    {
        // Folder picker is not available in Photino. Return the current/active
        // instance root so the frontend can show it and collect user input manually.
        return Task.FromResult<string?>(_instanceService.GetInstanceRoot());
    }
    
    /// <summary>
    /// Opens a folder browser dialog and returns the selected path.
    /// </summary>
    public async Task<string?> BrowseFolder(string? initialPath = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = $@"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; ";
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    script += $@"$dialog.SelectedPath = '{initialPath.Replace("'", "''")}'; ";
                script += @"if ($dialog.ShowDialog() -eq 'OK') { $dialog.SelectedPath }";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var initialDir = !string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath) 
                    ? $"default location \"{initialPath}\"" 
                    : "";
                    
                var script = $@"tell application ""Finder""
                    activate
                    set theFolder to choose folder with prompt ""Select Folder"" {initialDir}
                    return POSIX path of theFolder
                end tell";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else
            {
                // Linux - use zenity
                var args = "--file-selection --directory --title=\"Select Folder\"";
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    args += $" --filename=\"{initialPath}/\"";
                    
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse folder: {ex.Message}");
            return null;
        }
    }

    // News - matches Go implementation
    public async Task<List<NewsItemResponse>> GetNewsAsync(int count) => await _newsService.GetNewsAsync(count);
    
    /// <summary>
    /// Synchronous wrapper for GetNewsAsync to maintain compatibility with frontend.
    /// </summary>
    public Task<List<NewsItemResponse>> GetNews(int count) => GetNewsAsync(count);

    /// <summary>
    /// Cleans news excerpt by removing HTML tags, duplicate title, and date prefixes.
    /// From PR #294
    /// </summary>
    // Update - download latest launcher per platform instead of in-place update
    public async Task<bool> UpdateAsync(JsonElement[]? args) => 
        await _updateService.UpdateAsync(args);

    // Browser
    public bool BrowserOpenURL(string url) => _browserService.OpenURL(url);

    // ========== Settings (delegated to SettingsService) ==========
    
    public bool GetMusicEnabled() => _settingsService.GetMusicEnabled();
    public bool SetMusicEnabled(bool enabled) => _settingsService.SetMusicEnabled(enabled);
    
    public string GetLauncherBranch() => _settingsService.GetLauncherBranch();
    public bool SetLauncherBranch(string branch) => _settingsService.SetLauncherBranch(branch);
    
    public bool GetCloseAfterLaunch() => _settingsService.GetCloseAfterLaunch();
    public bool SetCloseAfterLaunch(bool enabled) => _settingsService.SetCloseAfterLaunch(enabled);
    
    public bool GetShowDiscordAnnouncements() => _settingsService.GetShowDiscordAnnouncements();
    public bool SetShowDiscordAnnouncements(bool enabled) => _settingsService.SetShowDiscordAnnouncements(enabled);
    public bool IsAnnouncementDismissed(string announcementId) => _settingsService.IsAnnouncementDismissed(announcementId);
    public bool DismissAnnouncement(string announcementId) => _settingsService.DismissAnnouncement(announcementId);
    
    public bool GetDisableNews() => _settingsService.GetDisableNews();
    public bool SetDisableNews(bool disabled) => _settingsService.SetDisableNews(disabled);
    
    public string GetBackgroundMode() => _settingsService.GetBackgroundMode();
    public bool SetBackgroundMode(string mode) => _settingsService.SetBackgroundMode(mode);
    public List<string> GetAvailableBackgrounds() => _settingsService.GetAvailableBackgrounds();
    
    public string GetAccentColor() => _settingsService.GetAccentColor();
    public bool SetAccentColor(string color) => _settingsService.SetAccentColor(color);
    
    public bool GetHasCompletedOnboarding() => _settingsService.GetHasCompletedOnboarding();
    public bool SetHasCompletedOnboarding(bool completed) => _settingsService.SetHasCompletedOnboarding(completed);
    
    /// <summary>
    /// Generates a random username for the onboarding flow.
    /// </summary>
    public string GetRandomUsername() => UtilityService.GenerateRandomUsername();
    
    public bool ResetOnboarding() => _settingsService.ResetOnboarding();
    
    public bool GetOnlineMode() => _settingsService.GetOnlineMode();
    public bool SetOnlineMode(bool online) => _settingsService.SetOnlineMode(online);
    
    public string GetAuthDomain() => _settingsService.GetAuthDomain();
    public bool SetAuthDomain(string domain) => _settingsService.SetAuthDomain(domain);
    
    public string GetLauncherDataDirectory() => _settingsService.GetLauncherDataDirectory();
    public Task<string?> SetLauncherDataDirectoryAsync(string path) => _settingsService.SetLauncherDataDirectoryAsync(path);

    // Delegate to ModService
    public List<InstalledMod> GetInstanceInstalledMods(string instancePath) => 
        ModService.GetInstanceInstalledMods(instancePath);
    
    /// <summary>
    /// Convenience overload that gets installed mods by branch and version.
    /// </summary>
    public List<InstalledMod> GetInstanceInstalledMods(string branch, int version)
    {
        var instancePath = GetInstancePath(branch, version);
        return ModService.GetInstanceInstalledMods(instancePath);
    }
    
    /// <summary>
    /// Opens the instance folder in the file manager.
    /// </summary>
    public bool OpenInstanceFolder(string branch, int version) => _gameUtilityService.OpenInstanceFolder(branch, version);

    // CurseForge API constants
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int HytaleGameId = 70216; // Hytale game ID on CurseForge
    private const string CurseForgeApiKey = "$2a$10$bL4bIL5pUWqfcO7KQtnMReakwtfHbNKh6v1uTpKlzhwoueEJQnPnm";

    // Mod Manager with CurseForge API
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
        => await _modService.SearchModsAsync(query, page, pageSize, categories, sortField, sortOrder);

    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
        => await _modService.GetModFilesAsync(modId, page, pageSize);

    public async Task<List<ModCategory>> GetModCategoriesAsync()
        => await _modService.GetModCategoriesAsync();

    /// <summary>
    /// Browse for mod files using native OS dialog.
    /// Returns array of selected file paths or empty array if cancelled.
    /// </summary>
    public async Task<string[]> BrowseModFilesAsync() => await _fileDialogService.BrowseModFilesAsync();

    /// <summary>
    /// Triggers a test Discord announcement popup for developer testing.
    /// </summary>
    public DiscordAnnouncement? GetTestAnnouncement()
    {
        return new DiscordAnnouncement
        {
            Id = "test-announcement-" + DateTime.UtcNow.Ticks,
            AuthorName = "HyPrism Bot",
            AuthorAvatar = null,
            AuthorRole = "Developer",
            RoleColor = "#FFA845",
            Content = "🎉 This is a test announcement!\n\nThis is used to preview how Discord announcements will appear in the launcher. You can dismiss this by clicking the X button or disabling announcements.\n\n✨ Features:\n• Author info with avatar\n• Role colors\n• Images and attachments\n• Smooth animations",
            ImageUrl = null,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Sets the game language by copying translated language files to the game's language folder.
    /// Maps launcher locale codes to game locale codes and copies appropriate language files.
    /// </summary>
    /// <param name="languageCode">The launcher language code (e.g., "en", "es", "de", "fr")</param>
    /// <returns>True if language files were successfully copied, false otherwise</returns>
    public async Task<bool> SetGameLanguageAsync(string languageCode) => await _languageService.SetGameLanguageAsync(languageCode);

    /// <summary>
    /// Gets the list of available game languages that have translation files.
    /// </summary>
    public List<string> GetAvailableGameLanguages() => _languageService.GetAvailableGameLanguages();
    
    /// <summary>
    /// Gets the current music enabled state from configuration.
    /// </summary>
    public async Task<bool> GetMusicEnabledAsync()
    {
        await Task.CompletedTask;
        return _settingsService.GetMusicEnabled();
    }

    // ================== WRAPPER MODE METHODS ==================
    // Used for Flatpak/AppImage wrapper to install/launch HyPrism

    /// <summary>
    /// Wrapper Mode: Get status of the installed HyPrism binary and check for updates.
    /// Returns: { installed: bool, version: string, needsUpdate: bool, latestVersion: string }
    /// </summary>
    public async Task<Dictionary<string, object>> WrapperGetStatus()
    {
        var result = new Dictionary<string, object>
        {
            ["installed"] = false,
            ["version"] = "",
            ["needsUpdate"] = false,
            ["latestVersion"] = ""
        };

        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, "HyPrism");
            var versionFile = Path.Combine(wrapperDir, "version.txt");

            if (!File.Exists(binaryPath))
            {
                return result;
            }

            result["installed"] = true;

            if (File.Exists(versionFile))
            {
                result["version"] = (await File.ReadAllTextAsync(versionFile)).Trim();
            }

            // Check GitHub for latest release
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (!string.IsNullOrEmpty(latestVersion))
            {
                result["latestVersion"] = latestVersion;
                result["needsUpdate"] = result["version"].ToString() != latestVersion;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperGetStatus error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Wrapper Mode: Install or update the latest HyPrism binary from GitHub releases.
    /// Downloads the appropriate release for the current OS and extracts it to wrapper directory.
    /// </summary>
    public async Task<bool> WrapperInstallLatest()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            Directory.CreateDirectory(wrapperDir);

            // Get latest release from GitHub
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (string.IsNullOrEmpty(latestVersion))
            {
                Console.WriteLine("Failed to get latest version from GitHub");
                return false;
            }

            // Determine the asset name based on OS
            string assetName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                assetName = $"HyPrism-{latestVersion}-linux-x64.tar.gz";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                assetName = $"HyPrism-{latestVersion}-win-x64.zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                assetName = $"HyPrism-{latestVersion}-osx-x64.tar.gz";
            }
            else
            {
                Console.WriteLine($"Unsupported platform: {RuntimeInformation.OSDescription}");
                return false;
            }

            var downloadUrl = $"https://github.com/yyyumeniku/HyPrism/releases/download/{latestVersion}/{assetName}";
            var archivePath = Path.Combine(wrapperDir, assetName);

            // Download archive
            _progressNotificationService.SendProgress("wrapper-install", 0, "Downloading HyPrism...", 0, 100);
            
            var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download: {response.StatusCode}");
                return false;
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(archivePath))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            _progressNotificationService.SendProgress("wrapper-install", 50, "Extracting...", 50, 100);

            // Extract archive
            if (assetName.EndsWith(".tar.gz"))
            {
                await ExtractTarGz(archivePath, wrapperDir);
            }
            else if (assetName.EndsWith(".zip"))
            {
                ZipFile.ExtractToDirectory(archivePath, wrapperDir, true);
            }

            // Set executable permission on Linux/Mac
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binaryPath = Path.Combine(wrapperDir, "HyPrism");
                if (File.Exists(binaryPath))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{binaryPath}\"",
                            UseShellExecute = false
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
            }

            // Save version
            await File.WriteAllTextAsync(Path.Combine(wrapperDir, "version.txt"), latestVersion);

            // Cleanup archive
            File.Delete(archivePath);

            _progressNotificationService.SendProgress("wrapper-install", 100, "Installation complete", 100, 100);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperInstallLatest error: {ex.Message}");
            _progressNotificationService.SendErrorEvent("Wrapper Installation Error", ex.Message, null);
            return false;
        }
    }

    /// <summary>
    /// Wrapper Mode: Launch the installed HyPrism binary.
    /// </summary>
    public async Task<bool> WrapperLaunch()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HyPrism.exe" : "HyPrism");

            if (!File.Exists(binaryPath))
            {
                Console.WriteLine("HyPrism binary not found");
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    UseShellExecute = true,
                    WorkingDirectory = wrapperDir
                }
            };

            process.Start();
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperLaunch error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Helper: Extract .tar.gz archive (for Linux/Mac releases).
    /// </summary>
    private static async Task ExtractTarGz(string archivePath, string destinationDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destinationDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Failed to extract tar.gz: {error}");
        }
    }

    /// <summary>
    /// Helper: Get latest launcher version from GitHub releases API.
    /// </summary>
    private async Task<string> GetLatestLauncherVersionFromGitHub()
    {
        try
        {
            var response = await HttpClient.GetStringAsync("https://api.github.com/repos/yyyumeniku/HyPrism/releases/latest");
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagName))
            {
                return tagName.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get latest version: {ex.Message}");
        }
        return "";
    }

    // ================== OPTIMIZATION MODS ==================

    /// <summary>
    /// Installs a predefined list of optimization mods to the current game instance.
    /// Optimization mods improve game performance and FPS.
    /// TODO: Define actual optimization mod list when Hytale mod ecosystem is available.
    /// </summary>
    public async Task<bool> InstallOptimizationModsAsync()
    {
        try
        {
            // Predefined list of optimization mod IDs from CurseForge
            // TODO: Add actual mod IDs when Hytale optimization mods become available
            var optimizationModIds = new List<string>
            {
                // Example: "sodium", "lithium", "phosphor", "ferritecore", etc.
            };

            if (optimizationModIds.Count == 0)
            {
                Logger.Warning("OptimizationMods", "No optimization mods defined yet - feature is a stub");
                return false;
            }

            // Get current game instance path
            var branch = UtilityService.NormalizeVersionType(_config.VersionType);
            var instancePath = GetLatestInstancePath(branch);
            
            if (!_instanceService.IsClientPresent(instancePath))
            {
                Logger.Warning("OptimizationMods", "Game client not installed - cannot install optimization mods");
                return false;
            }

            // Install each optimization mod
            foreach (var modId in optimizationModIds)
            {
                try
                {
                    // Get latest mod file
                    var filesResult = await _modService.GetModFilesAsync(modId, 1, 10);
                    if (filesResult.Files.Count == 0) continue;

                    var latestFile = filesResult.Files[0];
                    
                    // Install mod to instance
                    await _modService.InstallModFileToInstanceAsync(
                        modId, 
                        latestFile.Id.ToString(), 
                        instancePath,
                        (stage, msg) => _progressNotificationService.SendProgress(
                            "optimization-mods", 
                            0, 
                            $"Installing {latestFile.DisplayName}: {msg}", 
                            0, 
                            100
                        )
                    );
                    
                    Logger.Success("OptimizationMods", $"Installed optimization mod: {latestFile.DisplayName}");
                }
                catch (Exception ex)
                {
                    Logger.Warning("OptimizationMods", $"Failed to install mod {modId}: {ex.Message}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("OptimizationMods", $"InstallOptimizationModsAsync error: {ex.Message}");
            _progressNotificationService.SendErrorEvent("Optimization Mods Installation Error", ex.Message, null);
            return false;
        }
    }
}
