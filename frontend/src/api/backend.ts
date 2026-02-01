// Backend API Wrapper
// Provides typed access to C# backend methods

import { callBackend } from './bridge';
import { mods, app, config, news, updater } from './models';

/**
 * Information about a pending game update
 */
export interface UpdateInfo {
    /** The version number being updated from */
    OldVersion: number;
    /** The target version number */
    NewVersion: number;
    /** Whether the old version has user data that can be migrated */
    HasOldUserData: boolean;
    /** The branch (Release/Beta) */
    Branch: string;
}

/**
 * Detailed information about an installed game version
 */
export interface InstalledVersionInfo {
    /** The version ID (e.g. 0 for latest) */
    Version: number;
    /** The branch name */
    Branch: string;
    /** Absolute path to the instance folder */
    Path: string;
    /** Size of the UserData folder in bytes */
    UserDataSize: number;
    /** Whether UserData exists */
    HasUserData: boolean;
}

/**
 * User profile definition
 */
export interface Profile {
    /** Unique profile identifier */
    id: string;
    /** Player UUID */
    uuid: string;
    /** Display name */
    name: string;
    /** Creation timestamp */
    createdAt: string;
}

/**
 * Avatar customization configuration
 */
export interface SkinConfig {
    name?: string;
    bodyCharacteristic?: string;
    cape?: string;
    earAccessory?: string;
    ears?: string;
    eyebrows?: string;
    eyes?: string;
    eyeColor?: string;
    face?: string;
    faceAccessory?: string;
    facialHair?: string;
    gloves?: string;
    haircut?: string;
    hairColor?: string;
    headAccessory?: string;
    mouth?: string;
    overpants?: string;
    overtop?: string;
    pants?: string;
    shoes?: string;
    skinFeature?: string;
    skinColor?: string;
    undertop?: string;
    underwear?: string;
    /** Map of part name to hex color reference */
    colors?: Record<string, string>;
}

/**
 * Announcement fetched from Discord
 */
export interface DiscordAnnouncement {
    /** Message ID */
    Id: string;
    /** Message content (markdown) */
    Content: string;
    /** Author username */
    AuthorName: string;
    /** Author avatar URL */
    AuthorAvatar?: string;
    /** Author role name */
    AuthorRole?: string;
    /** Role color hex */
    RoleColor?: string;
    /** Attached image URL */
    ImageUrl?: string;
    /** Creation timestamp */
    Timestamp: string;
}

/**
 * Rosetta 2 emulation status (macOS only)
 */
export interface RosettaStatus {
    /** Whether Rosetta needs installation */
    NeedsInstall: boolean;
    /** Status message */
    Message: string;
    /** Command to install Rosetta */
    Command: string;
    /** Help URL */
    TutorialUrl?: string;
}

// #region Mods
/**
 * Checks for updates for mods installed in a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const CheckInstanceModUpdates = (branch: string, version: number) => callBackend<Array<mods.Mod>>('CheckInstanceModUpdates', branch, version);

/**
 * Checks updates for all known mods
 */
export const CheckModUpdates = () => callBackend<Array<mods.Mod>>('CheckModUpdates');

/**
 * Gets a list of all installed mods across all instances
 */
export const GetInstalledMods = () => callBackend<Array<mods.Mod>>('GetInstalledMods');

/**
 * Gets mods installed in a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const GetInstanceInstalledMods = (branch: string, version: number) => callBackend<Array<mods.Mod>>('GetInstanceInstalledMods', branch, version);

/**
 * Gets available mod categories
 */
export const GetModCategories = () => callBackend<Array<mods.ModCategory>>('GetModCategories');

/**
 * Gets detailed information about a specific mod
 * @param modId - The Mod ID
 */
export const GetModDetails = (modId: number) => callBackend<mods.CurseForgeMod>('GetModDetails', modId);

/**
 * Gets files available for a mod
 * @param modId - The Mod ID (string)
 * @param page - Page number
 * @param pageSize - Items per page
 */
export const GetModFiles = (modId: string, page = 0, pageSize = 50) => callBackend<Array<mods.ModFile>>('GetModFiles', modId, page, pageSize);

/**
 * Installs a specific mod (latest file)
 * @param modId - The Mod ID
 */
export const InstallMod = (modId: number) => callBackend<void>('InstallMod', modId);

/**
 * Installs a specific file version of a mod
 * @param modId - The Mod ID
 * @param fileId - The File ID
 */
export const InstallModFile = (modId: number, fileId: number) => callBackend<void>('InstallModFile', modId, fileId);

/**
 * Installs a specific mod file to a specific instance
 * @param modId - The Mod ID
 * @param fileId - The File ID
 * @param branch - The game branch
 * @param version - The game version
 */
export const InstallModFileToInstance = (modId: string, fileId: string, branch: string, version: number) => callBackend<boolean>('InstallModFileToInstance', modId, fileId, branch, version);

/**
 * Installs a mod by slug to a specific version
 * @param modId - The Mod ID
 * @param slug - The mod slug
 * @param version - The game version
 */
export const InstallModToInstance = (modId: number, slug: string, version: number) => callBackend<void>('InstallModToInstance', modId, slug, version);

/**
 * Installs a mod from a local file path
 * @param sourcePath - Path to the local mod file
 * @param branch - The game branch
 * @param version - The game version
 */
export const InstallLocalModFile = (sourcePath: string, branch: string, version: number) => callBackend<boolean>('InstallLocalModFile', sourcePath, branch, version);

/**
 * Installs a mod from a Base64 string
 * @param fileName - Name of the file
 * @param base64Content - File content encoded in Base64
 * @param branch - The game branch
 * @param version - The game version
 */
export const InstallModFromBase64 = (fileName: string, base64Content: string, branch: string, version: number) => callBackend<boolean>('InstallModFromBase64', fileName, base64Content, branch, version);

/**
 * Search for mods
 * @param query - Search term
 * @param page - Page number
 * @param pageSize - Results per page
 * @param categories - Filter by category IDs
 * @param sortField - Sort field ID
 * @param sortOrder - Sort order ID
 */
export const SearchMods = (query: string, page: number, pageSize: number, categories: Array<string> = [], sortField = 2, sortOrder = 2) => callBackend<mods.SearchResult>('SearchMods', query, page, pageSize, categories, sortField, sortOrder);

/**
 * Enables or disables a mod in a specific instance
 * @param modId - The Mod ID
 * @param enabled - New state
 * @param branch - The game branch
 * @param version - The game version
 */
export const ToggleInstanceMod = (modId: string, enabled: boolean, branch: string, version: number) => callBackend<void>('ToggleInstanceMod', modId, enabled, branch, version);

/**
 * Global toggle for a mod
 * @param modId - The Mod ID
 * @param enabled - New state
 */
export const ToggleMod = (modId: string, enabled: boolean) => callBackend<void>('ToggleMod', modId, enabled);

/**
 * Uninstalls a mod from a specific instance
 * @param modId - The Mod ID
 * @param branch - The game branch
 * @param version - The game version
 */
export const UninstallInstanceMod = (modId: string, branch: string, version: number) => callBackend<void>('UninstallInstanceMod', modId, branch, version);

/**
 * Completely uninstalls a mod
 * @param modId - The Mod ID
 */
export const UninstallMod = (modId: string) => callBackend<void>('UninstallMod', modId);

/**
 * Exports the list of mods from an instance to a file
 * @param branch - The game branch
 * @param version - The game version
 */
export const ExportModList = (branch: string, version: number) => callBackend<string | null>('ExportModList', branch, version);

/**
 * Exports mods to a folder
 * @param branch - The game branch
 * @param version - The game version
 * @param targetFolder - Destination folder path
 * @param exportType - Export structure type
 */
export const ExportModsToFolder = (branch: string, version: number, targetFolder: string, exportType: string) => callBackend<string | null>('ExportModsToFolder', branch, version, targetFolder, exportType);

/**
 * Gets the path of the last export operation
 */
export const GetLastExportPath = () => callBackend<string>('GetLastExportPath');

/**
 * Imports a mod list from a file
 * @param modListPath - Path to the mod list file
 * @param branch - The target branch
 * @param version - The target version
 */
export const ImportModList = (modListPath: string, branch: string, version: number) => callBackend<number>('ImportModList', modListPath, branch, version);

/**
 * Opens the global mods storage folder in file explorer
 */
export const OpenModsFolder = () => callBackend<void>('OpenModsFolder');

/**
 * Opens the mods folder for a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const OpenInstanceModsFolder = (branch: string, version: number) => callBackend<void>('OpenInstanceModsFolder', branch, version);

/**
 * Opens a file dialog to browse for mod files
 */
export const BrowseModFiles = () => callBackend<string[]>('BrowseModFiles');
// #endregion

// #region Game & Version
/**
 * Checks if the "Latest" instance needs an update
 * @param branch - The game branch
 */
export const CheckLatestNeedsUpdate = (branch: string) => callBackend<boolean>('CheckLatestNeedsUpdate', branch);

/**
 * Checks for launcher application updates
 */
export const CheckUpdate = () => callBackend<updater.Asset>('CheckUpdate');

/**
 * Checks availability of game versions from the update server
 */
export const CheckVersionAvailability = () => callBackend<app.VersionCheckInfo>('CheckVersionAvailability');

/**
 * Gets info about a pending update for the current instance type
 * @param branch - The game branch
 */
export const GetPendingUpdateInfo = (branch: string) => callBackend<UpdateInfo | null>('GetPendingUpdateInfo', branch);

/**
 * Gets a map of available versions and their IDs
 */
export const GetAvailableVersions = () => callBackend<Record<string, number>>('GetAvailableVersions');

/**
 * Gets the currently selected version string
 */
export const GetCurrentVersion = () => callBackend<string>('GetCurrentVersion');

/**
 * Gets a list of locally installed versions
 */
export const GetInstalledVersions = () => callBackend<Array<app.InstalledVersion>>('GetInstalledVersions');

/**
 * Gets installed version IDs for a specific branch
 * @param branch - The game branch
 */
export const GetInstalledVersionsForBranch = (branch: string) => callBackend<Array<number>>('GetInstalledVersionsForBranch', branch);

/**
 * Gets detailed info about installed versions including paths and sizes
 */
export const GetInstalledVersionsDetailed = () => callBackend<Array<InstalledVersionInfo>>('GetInstalledVersionsDetailed');

/**
 * Gets the ID of the currently selected version
 */
export const GetSelectedVersion = () => callBackend<number>('GetSelectedVersion');

/**
 * Gets available version IDs for a branch
 * @param branch - The game branch
 */
export const GetVersionList = (branch: string) => callBackend<Array<number>>('GetVersionList', branch);

/**
 * Gets the currently selected branch/version type
 */
export const GetVersionType = () => callBackend<string>('GetVersionType');

/**
 * Checks if any game version is installed
 */
export const IsGameInstalled = () => callBackend<boolean>('IsGameInstalled');

/**
 * Checks if the game is currently running
 */
export const IsGameRunning = () => callBackend<boolean>('IsGameRunning');

/**
 * Checks if a specific version is installed
 * @param branch - The game branch
 * @param version - The game version ID
 */
export const IsVersionInstalled = (branch: string, version: number) => callBackend<boolean>('IsVersionInstalled', branch, version);

/**
 * Sets the selected version ID
 * @param version - The version ID
 */
export const SetSelectedVersion = (version: number) => callBackend<void>('SetSelectedVersion', version);

/**
 * Sets the active branch type
 * @param type - The branch name (e.g. 'Release')
 */
export const SetVersionType = (type: string) => callBackend<void>('SetVersionType', type);

/**
 * Switches the active version
 * @param version - The version ID to switch to
 */
export const SwitchVersion = (version: number) => callBackend<void>('SwitchVersion', version);

/**
 * Deletes a game instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const DeleteGame = (branch: string, version: number) => callBackend<boolean>('DeleteGame', branch, version);

/**
 * Downloads the game version and launches it
 * @param version - The version string to display
 */
export const DownloadAndLaunch = (version: string) => callBackend<void>('DownloadAndLaunch', version);

/**
 * Only downloads a specific version without launching
 * @param branch - The game branch
 * @param version - The release name/version string
 */
export const DownloadVersion = (branch: string, version: string) => callBackend<void>('DownloadVersion', branch, version);

/**
 * Requests the game process to exit
 */
export const ExitGame = () => callBackend<void>('ExitGame');

/**
 * Cancels the ongoing download or update
 */
export const CancelDownload = () => callBackend<boolean>('CancelDownload');

/**
 * Checks if the assets zip exists for an instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const HasAssetsZip = (branch: string, version: number) => callBackend<boolean>('HasAssetsZip', branch, version);

/**
 * Gets the path to the assets zip file
 * @param branch - The game branch
 * @param version - The game version
 */
export const GetAssetsZipPath = (branch: string, version: number) => callBackend<string | null>('GetAssetsZipPath', branch, version);

/**
 * Exports an instance to a zip file
 * @param branch - The game branch
 * @param version - The game version
 */
export const ExportInstance = (branch: string, version: number) => callBackend<string | null>('ExportInstance', branch, version);

/**
 * Copies UserData from one version to another
 * @param branch - The game branch
 * @param fromVersion - Source version ID
 * @param toVersion - Target version ID
 */
export const CopyUserData = (branch: string, fromVersion: number, toVersion: number) => callBackend<boolean>('CopyUserData', branch, fromVersion, toVersion);
//#endregion

// #region Configuration
/**
 * Gets the full application configuration
 */
export const GetConfig = () => callBackend<config.Config>('GetConfig');

/**
 * Saves the current configuration to disk
 */
export const SaveConfig = () => callBackend<void>('SaveConfig');

/**
 * Gets the custom directory path for instances
 */
export const GetCustomInstanceDir = () => callBackend<string>('GetCustomInstanceDir');

/**
 * Sets the custom directory for instances
 * @param dir - Absolute path
 */
export const SetCustomInstanceDir = (dir: string) => callBackend<void>('SetCustomInstanceDir', dir);

/**
 * Gets skin config for a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const GetInstanceSkinConfig = (branch: string, version: number) => callBackend<SkinConfig | null>('GetInstanceSkinConfig', branch, version);

/**
 * Gets the global/current skin config
 */
export const GetSkinConfig = () => callBackend<SkinConfig | null>('GetSkinConfig');

/**
 * Saves the skin configuration
 * @param config - The skin configuration object
 */
export const SaveSkinConfig = (config: SkinConfig) => callBackend<boolean>('SaveSkinConfig', config);

/**
 * Applies the current skin config to a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const ApplySkinToInstance = (branch: string, version: number) => callBackend<boolean>('ApplySkinToInstance', branch, version);

/**
 * Gets the saved player nickname
 */
export const GetNick = () => callBackend<string>('GetNick');

/**
 * Sets the player nickname
 * @param nick - The new nickname
 */
export const SetNick = (nick: string) => callBackend<void>('SetNick', nick);

/**
 * Gets the saved player UUID
 */
export const GetUUID = () => callBackend<string>('GetUUID');

/**
 * Sets the player UUID
 * @param uuid - The new UUID
 */
export const SetUUID = (uuid: string) => callBackend<boolean>('SetUUID', uuid);

/**
 * Checks if online mode (verification) is enabled
 */
export const GetOnlineMode = () => callBackend<boolean>('GetOnlineMode');

/**
 * Sets online mode status
 * @param enabled - Enable/Disable
 */
export const SetOnlineMode = (enabled: boolean) => callBackend<void>('SetOnlineMode', enabled);

/**
 * Gets the authentication domain
 */
export const GetAuthDomain = () => callBackend<string>('GetAuthDomain');

/**
 * Sets the authentication domain
 * @param domain - Domain string
 */
export const SetAuthDomain = (domain: string) => callBackend<void>('SetAuthDomain', domain);

/**
 * Checks if background music is enabled
 */
export const GetMusicEnabled = () => callBackend<boolean>('GetMusicEnabled');

/**
 * Sets background music state
 * @param enabled - Enable/Disable
 */
export const SetMusicEnabled = (enabled: boolean) => callBackend<boolean>('SetMusicEnabled', enabled);

/**
 * Checks if "Latest" version should update automatically
 */
export const GetAutoUpdateLatest = () => callBackend<boolean>('GetAutoUpdateLatest');

/**
 * Sets auto-update behavior for "Latest" version
 * @param enabled - Enable/Disable
 */
export const SetAutoUpdateLatest = (enabled: boolean) => callBackend<void>('SetAutoUpdateLatest', enabled);

/**
 * Opens a folder picker dialog to select instance directory
 */
export const SelectInstanceDirectory = () => callBackend<string>('SelectInstanceDirectory');

/**
 * Sets the instance directory path
 * @param path - New path
 */
export const SetInstanceDirectory = (path: string) => callBackend<string>('SetInstanceDirectory', path);

/**
 * Opens a folder browser dialog
 * @param initialPath - Path to start in
 */
export const BrowseFolder = (initialPath?: string | null) => callBackend<string | null>('BrowseFolder', initialPath);

/**
 * Checks if launcher should close after game launch
 */
export const GetCloseAfterLaunch = () => callBackend<boolean>('GetCloseAfterLaunch');

/**
 * Sets close after launch behavior
 * @param enabled - Enable/Disable
 */
export const SetCloseAfterLaunch = (enabled: boolean) => callBackend<boolean>('SetCloseAfterLaunch', enabled);

/**
 * Checks if news feed is disabled
 */
export const GetDisableNews = () => callBackend<boolean>('GetDisableNews');

/**
 * Sets news feed visibility
 * @param disabled - True to hide news
 */
export const SetDisableNews = (disabled: boolean) => callBackend<boolean>('SetDisableNews', disabled);

/**
 * Gets the current background display mode (e.g. 'slideshow')
 */
export const GetBackgroundMode = () => callBackend<string>('GetBackgroundMode');

/**
 * Sets the background display mode
 * @param mode - Mode string
 */
export const SetBackgroundMode = (mode: string) => callBackend<boolean>('SetBackgroundMode', mode);

/**
 * Gets list of available custom background images
 */
export const GetAvailableBackgrounds = () => callBackend<string[]>('GetAvailableBackgrounds');

/**
 * Gets the UI accent color
 */
export const GetAccentColor = () => callBackend<string>('GetAccentColor');

/**
 * Sets the UI accent color
 * @param color - Hex color code
 */
export const SetAccentColor = (color: string) => callBackend<boolean>('SetAccentColor', color);

/**
 * Gets the data directory for the launcher
 */
export const GetLauncherDataDirectory = () => callBackend<string>('GetLauncherDataDirectory');

/**
 * Sets the data directory for the launcher
 * @param path - New path
 */
export const SetLauncherDataDirectory = (path: string) => callBackend<string | null>('SetLauncherDataDirectory', path);

/**
 * Reads the latest game logs
 */
export const GetGameLogs = () => callBackend<string>('GetGameLogs');

/**
 * Gets a list of recent log files
 * @param count - Number of logs to retrieve
 */
export const GetRecentLogs = (count?: number) => callBackend<string[]>('GetRecentLogs', count);

/**
 * Unused/Legacy log retrieval
 */
export const GetLogs = () => callBackend<string>('GetLogs');

/**
 * Gets a list of crash reports
 */
export const GetCrashReports = () => callBackend<Array<app.CrashReport>>('GetCrashReports');

/**
 * Sets the game locale code
 * @param languageCode - Locale code (e.g. en-US)
 */
export const SetGameLanguage = (languageCode: string) => callBackend<boolean>('SetGameLanguage', languageCode);

/**
 * Gets available game languages
 */
export const GetAvailableGameLanguages = () => callBackend<string[]>('GetAvailableGameLanguages');
// #endregion

// #region Profiles
/**
 * Gets all saved profiles
 */
export const GetProfiles = () => callBackend<Profile[]>('GetProfiles');

/**
 * Gets the index of the active profile
 */
export const GetActiveProfileIndex = () => callBackend<number>('GetActiveProfileIndex');

/**
 * Creates a new profile
 * @param name - Profile name
 * @param uuid - Profile UUID
 */
export const CreateProfile = (name: string, uuid: string) => callBackend<Profile | null>('CreateProfile', name, uuid);

/**
 * Deletes a profile
 * @param profileId - Profile ID
 */
export const DeleteProfile = (profileId: string) => callBackend<boolean>('DeleteProfile', profileId);

/**
 * Switches the active profile
 * @param index - Index in profile list
 */
export const SwitchProfile = (index: number) => callBackend<boolean>('SwitchProfile', index);

/**
 * Updates an existing profile
 * @param profileId - Profile ID
 * @param newName - New name (optional)
 * @param newUuid - New UUID (optional)
 */
export const UpdateProfile = (profileId: string, newName: string | null, newUuid: string | null) => callBackend<boolean>('UpdateProfile', profileId, newName, newUuid);

/**
 * Saves current settings as a new profile
 */
export const SaveCurrentAsProfile = () => callBackend<Profile | null>('SaveCurrentAsProfile');

/**
 * Gets the base64 avatar preview for current user
 */
export const GetAvatarPreview = () => callBackend<string | null>('GetAvatarPreview');

/**
 * Gets avatar preview for a specific UUID
 * @param uuid - Player UUID
 */
export const GetAvatarPreviewForUUID = (uuid: string) => callBackend<string | null>('GetAvatarPreviewForUUID', uuid);

/**
 * Clears the avatar cache
 */
export const ClearAvatarCache = () => callBackend<boolean>('ClearAvatarCache');

// System & Launcher

/**
 * Gets the current launcher version string
 */
export const GetLauncherVersion = () => callBackend<string>('GetLauncherVersion');

/**
 * Gets the current launcher update branch (stable/beta)
 */
export const GetLauncherBranch = () => callBackend<string>('GetLauncherBranch');

/**
 * Sets the launcher update branch
 * @param branch - Branch name
 */
export const SetLauncherBranch = (branch: string) => callBackend<boolean>('SetLauncherBranch', branch);

/**
 * Checks Rosetta 2 status (MacOS)
 */
export const CheckRosettaStatus = () => callBackend<RosettaStatus | null>('CheckRosettaStatus');

/**
 * Opens the current instance folder
 */
export const OpenFolder = () => callBackend<void>('OpenFolder');

/**
 * Opens the folder for a specific instance
 * @param branch - The game branch
 * @param version - The game version
 */
export const OpenInstanceFolder = (branch: string, version: number) => callBackend<boolean>('OpenInstanceFolder', branch, version);

/**
 * Opens the game executable folder
 */
export const OpenGameFolder = () => callBackend<void>('OpenGameFolder');

/**
 * Opens the launcher application folder
 */
export const OpenLauncherFolder = () => callBackend<void>('OpenLauncherFolder');

/**
 * Gets the absolute path to the launcher folder
 */
export const GetLauncherFolderPath = () => callBackend<string>('GetLauncherFolderPath');

/**
 * Deletes all launcher data
 */
export const DeleteLauncherData = () => callBackend<boolean>('DeleteLauncherData');

/**
 * Quickly launches the last played instance
 */
export const QuickLaunch = () => callBackend<void>('QuickLaunch');

/**
 * Repairs the game installation
 */
export const RepairInstallation = () => callBackend<void>('RepairInstallation');

/**
 * Runs system diagnostics
 */
export const RunDiagnostics = () => callBackend<app.DiagnosticReport>('RunDiagnostics');

/**
 * Saves diagnostic report to a file
 */
export const SaveDiagnosticReport = () => callBackend<string>('SaveDiagnosticReport');

/**
 * Updates the launcher application
 */
export const Update = () => callBackend<void>('Update');

/**
 * Gets system platform information
 */
export const GetPlatformInfo = () => callBackend<Record<string, string>>('GetPlatformInfo');

/**
 * Gets the full path to the game executable
 */
export const GetGamePath = () => callBackend<string>('GetGamePath');

// Onboarding

/**
 * Checks if user has completed the onboarding flow
 */
export const GetHasCompletedOnboarding = () => callBackend<boolean>('GetHasCompletedOnboarding');

/**
 * Sets onboarding completion status
 * @param completed - Status
 */
export const SetHasCompletedOnboarding = (completed: boolean) => callBackend<boolean>('SetHasCompletedOnboarding', completed);

/**
 * Generates a random username
 */
export const GetRandomUsername = () => callBackend<string>('GetRandomUsername');

/**
 * Resets onboarding status
 */
export const ResetOnboarding = () => callBackend<boolean>('ResetOnboarding');
// #endregion

// #region News
/**
 * Fetches news items
 * @param count - Number of items to fetch
 */
export const GetNews = (count: number) => callBackend<Array<news.NewsItem>>('GetNews', count);
// #endregion

// #region Discord Announcements
/**
 * Checks if discord announcements should be shown
 */
export const GetShowDiscordAnnouncements = () => callBackend<boolean>('GetShowDiscordAnnouncements');

/**
 * Sets discord announcements visibility
 * @param enabled - Enable/Disable
 */
export const SetShowDiscordAnnouncements = (enabled: boolean) => callBackend<boolean>('SetShowDiscordAnnouncements', enabled);

/**
 * Marks an announcement as dismissed
 * @param announcementId - Announcement ID
 */
export const DismissAnnouncement = (announcementId: string) => callBackend<boolean>('DismissAnnouncement', announcementId);

/**
 * Sends a reaction to a Discord announcement
 * @param messageId - Message ID
 * @param emoji - Emoji name/char
 */
export const ReactToAnnouncement = (messageId: string, emoji: string) => callBackend<boolean>('ReactToAnnouncement', messageId, emoji);

/**
 * Gets the latest active Discord announcement
 */
export const GetDiscordAnnouncement = () => callBackend<DiscordAnnouncement | null>('GetDiscordAnnouncement');

/**
 * Gets a test/mock Discord announcement
 */
export const GetTestAnnouncement = () => callBackend<DiscordAnnouncement | null>('GetTestAnnouncement');
// #endregion

// #region Window
/**
 * Closes the application window
 */
export const WindowClose = () => callBackend<void>('WindowClose');
// #endregion