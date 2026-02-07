using HyPrism.Models;

namespace HyPrism.Services.Core;

/// <summary>
/// Service responsible for managing and dispatching progress notifications.
/// </summary>
public class ProgressNotificationService : IProgressNotificationService
{
    private readonly DiscordService _discordService;
    
    // Events
    public event Action<ProgressUpdateMessage>? DownloadProgressChanged;
    public event Action<string, int>? GameStateChanged;
    public event Action<string, string, string?>? ErrorOccurred;
    
    public ProgressNotificationService(DiscordService discordService)
    {
        _discordService = discordService;
    }
    
    /// <summary>
    /// Sends progress update notification.
    /// </summary>
    public void SendProgress(string stage, int progress, string messageKey, object[]? args, long downloaded, long total)
    {
        var msg = new ProgressUpdateMessage 
        { 
            State = stage, 
            Progress = progress, 
            MessageKey = messageKey, 
            Args = args,
            DownloadedBytes = downloaded,
            TotalBytes = total
        };
        
        DownloadProgressChanged?.Invoke(msg);
        
        // Don't update Discord during download/install to avoid showing extraction messages
        // Only update on complete or idle
        if (stage == "complete")
        {
            _discordService.SetPresence(DiscordService.PresenceState.Idle);
        }
    }

    public void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0) 
        => SendProgress(stage, progress, messageKey, args, downloaded, total);
    
    /// <summary>
    /// Sends game state change notification.
    /// </summary>
    public void SendGameStateEvent(string state, int? exitCode = null)
    {
        switch (state)
        {
            case "starting":
                GameStateChanged?.Invoke(state, 0);
                break;
            case "running":
                GameStateChanged?.Invoke(state, 0);
                _discordService.SetPresence(DiscordService.PresenceState.Playing);
                break;
            case "stopped":
                GameStateChanged?.Invoke(state, exitCode ?? 0);
                _discordService.SetPresence(DiscordService.PresenceState.Idle);
                break;
        }
    }

    public void ReportGameStateChanged(string state, int? exitCode = null) => SendGameStateEvent(state, exitCode);

    public void SendErrorEvent(string type, string message, string? technical = null)
    {
        ErrorOccurred?.Invoke(type, message, technical);
    }
    
    public void ReportError(string type, string message, string? technical = null) 
        => SendErrorEvent(type, message, technical);
}
