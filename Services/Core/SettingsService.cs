namespace HyPrism.Services.Core;

/// <summary>
/// Manages all launcher settings (preferences, UI config, behavior options).
/// Provides centralized access to configuration properties with automatic persistence.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ConfigService _configService;
    private readonly LocalizationService _localizationService;
    
    public SettingsService(ConfigService configService, LocalizationService localizationService)
    {
        _configService = configService;
        _localizationService = localizationService;
        
        // Apply initial language override from config
        var savedLang = _configService.Configuration.Language;
        if (!string.IsNullOrEmpty(savedLang))
        {
            _localizationService.CurrentLanguage = savedLang;
        }
    }
    
    // Events
    public event Action<string>? OnAccentColorChanged;
    public event Action<string?>? OnBackgroundChanged;
    
    // ========== Localization Settings (Language) ==========
    
    public string GetLanguage() => _configService.Configuration.Language;

    public bool SetLanguage(string languageCode)
    {
        var availableLanguages = LocalizationService.GetAvailableLanguages();
        if (availableLanguages.ContainsKey(languageCode))
        {
            _configService.Configuration.Language = languageCode;
            // Update the localization service which drives the UI
            _localizationService.CurrentLanguage = languageCode;
            _configService.SaveConfig();
            Logger.Info("Config", $"Language changed to: {languageCode}");
            return true;
        }
        return false;
    }

    // ========== Music Settings ==========
    
    public bool GetMusicEnabled() => _configService.Configuration.MusicEnabled;
    
    public bool SetMusicEnabled(bool enabled)
    {
        _configService.Configuration.MusicEnabled = enabled;
        _configService.SaveConfig();
        return true;
    }

    // ========== Launcher Branch (release/beta update channel) ==========
    
    public string GetLauncherBranch() => _configService.Configuration.LauncherBranch;
    
    public bool SetLauncherBranch(string branch)
    {
        var normalizedBranch = branch?.ToLowerInvariant() ?? "release";
        if (normalizedBranch != "release" && normalizedBranch != "beta")
        {
            normalizedBranch = "release";
        }
        
        if (_configService.Configuration.LauncherBranch == normalizedBranch)
        {
            return false;
        }
        
        _configService.Configuration.LauncherBranch = normalizedBranch;
        _configService.SaveConfig();
        Logger.Info("Config", $"Launcher branch set to: {normalizedBranch}");
        return true;
    }

    // ========== Close After Launch Setting ==========
    
    public bool GetCloseAfterLaunch() => _configService.Configuration.CloseAfterLaunch;
    
    public bool SetCloseAfterLaunch(bool enabled)
    {
        _configService.Configuration.CloseAfterLaunch = enabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"Close after launch set to: {enabled}");
        return true;
    }

    // ========== Discord Announcements Settings ==========
    
    public bool GetShowDiscordAnnouncements() => _configService.Configuration.ShowDiscordAnnouncements;
    
    public bool SetShowDiscordAnnouncements(bool enabled)
    {
        _configService.Configuration.ShowDiscordAnnouncements = enabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"Show Discord announcements set to: {enabled}");
        return true;
    }

    public bool IsAnnouncementDismissed(string announcementId)
    {
        return _configService.Configuration.DismissedAnnouncementIds.Contains(announcementId);
    }

    public bool DismissAnnouncement(string announcementId)
    {
        var config = _configService.Configuration;
        if (!config.DismissedAnnouncementIds.Contains(announcementId))
        {
            config.DismissedAnnouncementIds.Add(announcementId);
            _configService.SaveConfig();
            Logger.Info("Discord", $"Announcement {announcementId} dismissed");
        }
        return true;
    }

    // ========== News Settings ==========
    
    public bool GetDisableNews() => _configService.Configuration.DisableNews;
    
    public bool SetDisableNews(bool disabled)
    {
        _configService.Configuration.DisableNews = disabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"News disabled set to: {disabled}");
        return true;
    }

    // ========== Background Settings ==========
    
    public string GetBackgroundMode() => _configService.Configuration.BackgroundMode;
    
    public bool SetBackgroundMode(string mode)
    {
        _configService.Configuration.BackgroundMode = mode;
        _configService.SaveConfig();
        OnBackgroundChanged?.Invoke(mode);
        Logger.Info("Config", $"Background mode set to: {mode}");
        return true;
    }

    /// <summary>
    /// Gets the list of available background filenames (paths relative to asset root or names)
    /// </summary>
    public List<string> GetAvailableBackgrounds()
    {
        var backgrounds = new List<string>();
        // All backgrounds are now JPG format (PNG converted to save space)
        var jpgs = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30 };

        foreach (var i in jpgs) backgrounds.Add($"bg_{i}.jpg");
        
        return backgrounds.OrderBy(x => 
        {
            var num = int.Parse(System.Text.RegularExpressions.Regex.Match(x, @"\d+").Value);
            return num;
        }).ToList();
    }

    // ========== Accent Color Settings ==========
    
    public string GetAccentColor() => _configService.Configuration.AccentColor;
    
    public bool SetAccentColor(string color)
    {
        _configService.Configuration.AccentColor = color;
        _configService.SaveConfig();
        OnAccentColorChanged?.Invoke(color);
        Logger.Info("Config", $"Accent color set to: {color}");
        return true;
    }

    // ========== Onboarding State ==========
    
    public bool GetHasCompletedOnboarding() => _configService.Configuration.HasCompletedOnboarding;
    
    public bool SetHasCompletedOnboarding(bool completed)
    {
        _configService.Configuration.HasCompletedOnboarding = completed;
        _configService.SaveConfig();
        Logger.Info("Config", $"Onboarding completed: {completed}");
        return true;
    }

    /// <summary>
    /// Resets the onboarding so it will show again on next launch.
    /// </summary>
    public bool ResetOnboarding()
    {
        _configService.Configuration.HasCompletedOnboarding = false;
        _configService.SaveConfig();
        Logger.Info("Config", "Onboarding reset - will show on next launch");
        return true;
    }

    // ========== Online Mode Settings ==========
    
    public bool GetOnlineMode() => _configService.Configuration.OnlineMode;
    
    public bool SetOnlineMode(bool online)
    {
        _configService.Configuration.OnlineMode = online;
        _configService.SaveConfig();
        Logger.Info("Config", $"Online mode set to: {online}");
        return true;
    }
    
    // ========== Auth Domain Settings ==========
    
    public string GetAuthDomain() => _configService.Configuration.AuthDomain;
    
    public bool SetAuthDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "sessions.sanasol.ws";
        }
        _configService.Configuration.AuthDomain = domain;
        _configService.SaveConfig();
        Logger.Info("Config", $"Auth domain set to: {domain}");
        return true;
    }

    // ========== Launcher Data Directory Settings ==========
    
    public string GetLauncherDataDirectory() => _configService.Configuration.LauncherDataDirectory;
    
    public Task<string?> SetLauncherDataDirectoryAsync(string path)
    {
        try
        {
            // If path is empty or whitespace, clear the custom launcher data directory
            if (string.IsNullOrWhiteSpace(path))
            {
                _configService.Configuration.LauncherDataDirectory = "";
                _configService.SaveConfig();
                Logger.Success("Config", "Launcher data directory cleared, will use default on next restart");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(expanded);
            }

            // Just save the path, the change takes effect on next restart
            _configService.Configuration.LauncherDataDirectory = expanded;
            _configService.SaveConfig();

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
