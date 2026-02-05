using System;
using System.Collections.Generic;

namespace HyPrism.Models;

public class Config
{
    public string Version { get; set; } = "2.0.0";
    public string UUID { get; set; } = "";
    public string Nick { get; set; } = "Hyprism";
    public string VersionType { get; set; } = "release";
    public int SelectedVersion { get; set; } = 0;
    public string InstanceDirectory { get; set; } = "";
    public bool MusicEnabled { get; set; } = true;
    
    /// <summary>
    /// Launcher update channel: "release" for stable updates, "beta" for beta updates.
    /// Beta releases are named like "beta3-3.0.0" on GitHub.
    /// </summary>
    public string LauncherBranch { get; set; } = "release";
    
    /// <summary>
    /// If true, the launcher will close after successfully launching the game.
    /// </summary>
    public bool CloseAfterLaunch { get; set; } = false;
    
    /// <summary>
    /// If true, Discord announcements will be shown in the launcher.
    /// </summary>
    public bool ShowDiscordAnnouncements { get; set; } = true;
    
    /// <summary>
    /// List of Discord announcement IDs that have been dismissed by the user.
    /// </summary>
    public List<string> DismissedAnnouncementIds { get; set; } = new();
    
    /// <summary>
    /// If true, news will not be fetched or displayed.
    /// </summary>
    public bool DisableNews { get; set; } = false;

    /// <summary>
    /// Accent color for the UI (HEX code). Default is Hytale Orange (#FFA845).
    /// </summary>
    public string AccentColor { get; set; } = "#FFA845"; 
    
    /// <summary>
    /// Background mode: "auto" for rotating backgrounds, or a specific background filename.
    /// Changed from "slideshow" to "auto" in v2.0.4.
    /// </summary>
    public string BackgroundMode { get; set; } = "auto";
    
    /// <summary>
    /// Custom launcher data directory. If set, overrides the default app data location.
    /// </summary>
    public string LauncherDataDirectory { get; set; } = "";
    
    /// <summary>
    /// Current interface language code (e.g., "en-US", "ru-RU", "de-DE")
    /// </summary>
    public string Language { get; set; } = "en-US";
    
    /// <summary>
    /// If true, game will run in online mode (requires authentication).
    /// If false, game runs in offline mode.
    /// </summary>
    public bool OnlineMode { get; set; } = true;
    
    /// <summary>
    /// Auth server domain for online mode (e.g., "sessions.sanasol.ws").
    /// </summary>
    public string AuthDomain { get; set; } = "sessions.sanasol.ws";
    
    /// <summary>
    /// Last directory used for mod export. Defaults to Desktop.
    /// </summary>
    public string LastExportPath { get; set; } = "";
    
    /// <summary>
    /// If true, show alpha/beta mods in mod search results.
    /// </summary>
    public bool ShowAlphaMods { get; set; } = false;
    
    /// <summary>
    /// List of saved profiles (UUID, name pairs).
    /// </summary>
    public List<Profile> Profiles { get; set; } = new();
    
    /// <summary>
    /// Index of the currently active profile. -1 means no profile selected (use UUID/Nick directly).
    /// </summary>
    public int ActiveProfileIndex { get; set; } = -1;
    
    /// <summary>
    /// Whether the user has completed the initial onboarding flow.
    /// </summary>
    public bool HasCompletedOnboarding { get; set; } = false;
    
    /// <summary>
    /// Username to UUID mappings. Each username gets a consistent UUID across sessions.
    /// This ensures skins persist when changing usernames - switching back uses the same UUID.
    /// Keys are case-insensitive for lookup but preserve original casing.
    /// </summary>
    public Dictionary<string, string> UserUuids { get; set; } = new();
}
