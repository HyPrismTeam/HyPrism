using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HyPrism.Backend.Models;

namespace HyPrism.Backend.Services.Core;

/// <summary>
/// Manages all launcher settings (preferences, UI config, behavior options).
/// Provides centralized access to configuration properties with automatic persistence.
/// </summary>
public class SettingsService
{
    private readonly Func<Config> _getConfig;
    private readonly Action _saveConfig;
    
    public SettingsService(Func<Config> getConfig, Action saveConfig)
    {
        _getConfig = getConfig;
        _saveConfig = saveConfig;
    }
    
    // ========== Music Settings ==========
    
    public bool GetMusicEnabled() => _getConfig().MusicEnabled;
    
    public bool SetMusicEnabled(bool enabled)
    {
        _getConfig().MusicEnabled = enabled;
        _saveConfig();
        return true;
    }

    // ========== Launcher Branch (release/beta update channel) ==========
    
    public string GetLauncherBranch() => _getConfig().LauncherBranch;
    
    public bool SetLauncherBranch(string branch)
    {
        var normalizedBranch = branch?.ToLowerInvariant() ?? "release";
        if (normalizedBranch != "release" && normalizedBranch != "beta")
        {
            normalizedBranch = "release";
        }
        _getConfig().LauncherBranch = normalizedBranch;
        _saveConfig();
        Logger.Info("Config", $"Launcher branch set to: {normalizedBranch}");
        return true;
    }

    // ========== Close After Launch Setting ==========
    
    public bool GetCloseAfterLaunch() => _getConfig().CloseAfterLaunch;
    
    public bool SetCloseAfterLaunch(bool enabled)
    {
        _getConfig().CloseAfterLaunch = enabled;
        _saveConfig();
        Logger.Info("Config", $"Close after launch set to: {enabled}");
        return true;
    }

    // ========== Discord Announcements Settings ==========
    
    public bool GetShowDiscordAnnouncements() => _getConfig().ShowDiscordAnnouncements;
    
    public bool SetShowDiscordAnnouncements(bool enabled)
    {
        _getConfig().ShowDiscordAnnouncements = enabled;
        _saveConfig();
        Logger.Info("Config", $"Show Discord announcements set to: {enabled}");
        return true;
    }

    public bool IsAnnouncementDismissed(string announcementId)
    {
        return _getConfig().DismissedAnnouncementIds.Contains(announcementId);
    }

    public bool DismissAnnouncement(string announcementId)
    {
        var config = _getConfig();
        if (!config.DismissedAnnouncementIds.Contains(announcementId))
        {
            config.DismissedAnnouncementIds.Add(announcementId);
            _saveConfig();
            Logger.Info("Discord", $"Announcement {announcementId} dismissed");
        }
        return true;
    }

    // ========== News Settings ==========
    
    public bool GetDisableNews() => _getConfig().DisableNews;
    
    public bool SetDisableNews(bool disabled)
    {
        _getConfig().DisableNews = disabled;
        _saveConfig();
        Logger.Info("Config", $"News disabled set to: {disabled}");
        return true;
    }

    // ========== Background Settings ==========
    
    public string GetBackgroundMode() => _getConfig().BackgroundMode;
    
    public bool SetBackgroundMode(string mode)
    {
        _getConfig().BackgroundMode = mode;
        _saveConfig();
        Logger.Info("Config", $"Background mode set to: {mode}");
        return true;
    }

    /// <summary>
    /// Gets the list of available background filenames
    /// </summary>
    public List<string> GetAvailableBackgrounds()
    {
        var backgrounds = new List<string>();
        // These match the backgrounds in the frontend assets
        for (int i = 1; i <= 30; i++)
        {
            backgrounds.Add($"bg_{i}");
        }
        return backgrounds;
    }

    // ========== Accent Color Settings ==========
    
    public string GetAccentColor() => _getConfig().AccentColor;
    
    public bool SetAccentColor(string color)
    {
        _getConfig().AccentColor = color;
        _saveConfig();
        Logger.Info("Config", $"Accent color set to: {color}");
        return true;
    }

    // ========== Onboarding State ==========
    
    public bool GetHasCompletedOnboarding() => _getConfig().HasCompletedOnboarding;
    
    public bool SetHasCompletedOnboarding(bool completed)
    {
        _getConfig().HasCompletedOnboarding = completed;
        _saveConfig();
        Logger.Info("Config", $"Onboarding completed: {completed}");
        return true;
    }

    /// <summary>
    /// Resets the onboarding so it will show again on next launch.
    /// </summary>
    public bool ResetOnboarding()
    {
        _getConfig().HasCompletedOnboarding = false;
        _saveConfig();
        Logger.Info("Config", "Onboarding reset - will show on next launch");
        return true;
    }

    // ========== Online Mode Settings ==========
    
    public bool GetOnlineMode() => _getConfig().OnlineMode;
    
    public bool SetOnlineMode(bool online)
    {
        _getConfig().OnlineMode = online;
        _saveConfig();
        Logger.Info("Config", $"Online mode set to: {online}");
        return true;
    }
    
    // ========== Auth Domain Settings ==========
    
    public string GetAuthDomain() => _getConfig().AuthDomain;
    
    public bool SetAuthDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "sessions.sanasol.ws";
        }
        _getConfig().AuthDomain = domain;
        _saveConfig();
        Logger.Info("Config", $"Auth domain set to: {domain}");
        return true;
    }

    // ========== Launcher Data Directory Settings ==========
    
    public string GetLauncherDataDirectory() => _getConfig().LauncherDataDirectory;
    
    public Task<string?> SetLauncherDataDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom launcher data directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _getConfig().LauncherDataDirectory = "";
                _saveConfig();
                Logger.Success("Config", "Launcher data directory cleared, will use default on next restart");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(expanded);
            }

            // Just save the path, the change takes effect on next restart
            _getConfig().LauncherDataDirectory = expanded;
            _saveConfig();

            Logger.Success("Config", $"Launcher data directory set to {expanded} (takes effect on restart)");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set launcher data directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
}
