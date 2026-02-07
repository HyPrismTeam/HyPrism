namespace HyPrism.Services.Core;

public interface ISettingsService
{
    event Action<string?>? OnBackgroundChanged;
    event Action<string>? OnAccentColorChanged;
    
    string GetLanguage();
    bool SetLanguage(string language);
    bool GetMusicEnabled();
    bool SetMusicEnabled(bool enabled);
    string GetLauncherBranch();
    bool SetLauncherBranch(string branch);
    bool GetCloseAfterLaunch();
    bool SetCloseAfterLaunch(bool close);
    bool GetShowDiscordAnnouncements();
    bool SetShowDiscordAnnouncements(bool show);
    bool IsAnnouncementDismissed(string id);
    bool DismissAnnouncement(string id);
    bool GetDisableNews();
    bool SetDisableNews(bool disable);
    string GetBackgroundMode();
    bool SetBackgroundMode(string mode);
    List<string> GetAvailableBackgrounds();
    string GetAccentColor();
    bool SetAccentColor(string color);
    bool GetHasCompletedOnboarding();
    bool SetHasCompletedOnboarding(bool completed);
    bool ResetOnboarding();
    bool GetOnlineMode();
    bool SetOnlineMode(bool online);
    string GetAuthDomain();
    bool SetAuthDomain(string domain);
    string GetLauncherDataDirectory();
    Task<string?> SetLauncherDataDirectoryAsync(string directory);
}
