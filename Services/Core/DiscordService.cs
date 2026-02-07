using DiscordRPC;
using DiscordRPC.Logging;

namespace HyPrism.Services.Core;

/// <summary>
/// Silent logger for Discord RPC that suppresses connection error spam.
/// Only logs critical errors to HyPrism's logger.
/// </summary>
internal class SilentDiscordLogger : ILogger
{
    public LogLevel Level { get; set; } = LogLevel.Error;

    public void Trace(string message, params object[] args) { }
    public void Info(string message, params object[] args) { }
    public void Warning(string message, params object[] args) { }
    
    public void Error(string message, params object[] args)
    {
        // Only log if it's not a connection failure (those are expected when Discord is not running)
        if (!message.Contains("Failed connection") && !message.Contains("Failed to connect"))
        {
            Logger.Warning("Discord", string.Format(message, args));
        }
    }
}

public class DiscordService : IDiscordService, IDisposable
{
    private const string ApplicationId = "1464867466382540995";
    
    private DiscordRpcClient? _client;
    private bool _disposed;
    private bool _enabled;
    private DateTime _startTime;
    
    public enum PresenceState
    {
        Idle,
        Downloading,
        Installing,
        Playing
    }

    public void Initialize()
    {
        if (string.IsNullOrEmpty(ApplicationId))
        {
            Logger.Info("Discord", "Discord RPC disabled (no Application ID configured)");
            _enabled = false;
            return;
        }
        
        try
        {
            _client = new DiscordRpcClient(ApplicationId);
            // Use silent logger to suppress connection error spam
            _client.Logger = new SilentDiscordLogger();
            
            _client.OnReady += (sender, e) =>
            {
                Logger.Success("Discord", $"Connected to Discord as {e.User.Username}");
                _enabled = true;
            };
            
            _client.OnError += (sender, e) =>
            {
                // Only log if Discord was previously connected
                if (_enabled)
                {
                    Logger.Warning("Discord", $"Discord RPC error: {e.Message}");
                }
                _enabled = false;
            };
            
            _client.OnConnectionFailed += (sender, e) =>
            {
                // Silently disable RPC if Discord is not running (expected behavior)
                _enabled = false;
            };
            
            _client.Initialize();
            _startTime = DateTime.UtcNow;
            
            // Set initial idle presence
            SetPresence(PresenceState.Idle);
            
            Logger.Info("Discord", "Discord RPC initialized");
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to initialize Discord RPC: {ex.Message}");
            _enabled = false;
        }
    }

    public void SetPresence(PresenceState state, string? details = null, int? progress = null)
    {
        if (!_enabled || _client == null || !_client.IsInitialized) return;

        try
        {
            var assets = new Assets
            {
                LargeImageKey = "hyprism_logo",
                LargeImageText = "HyPrism Launcher",
                SmallImageKey = "hyprism_logo",
                SmallImageText = "HyPrism"
            };

            var presence = new RichPresence
            {
                Assets = assets
            };

            switch (state)
            {
                case PresenceState.Idle:
                    presence.Details = "In Launcher";
                    presence.State = "Browsing versions";
                    presence.Timestamps = new Timestamps(_startTime);
                    presence.Assets.SmallImageKey = "hyprism_logo";
                    presence.Assets.SmallImageText = "Idle";
                    break;

                case PresenceState.Downloading:
                    presence.Details = "Downloading Hytale";
                    presence.State = details ?? "Preparing...";
                    presence.Assets.SmallImageKey = "download";
                    presence.Assets.SmallImageText = "Downloading";
                    break;

                case PresenceState.Installing:
                    presence.Details = "Installing Hytale";
                    presence.State = details ?? "Extracting...";
                    presence.Assets.SmallImageKey = "install";
                    presence.Assets.SmallImageText = "Installing";
                    break;

                case PresenceState.Playing:
                    presence.Details = "Playing Hytale";
                    presence.State = details ?? "In Game";
                    presence.Timestamps = new Timestamps(DateTime.UtcNow);
                    presence.Assets.SmallImageKey = "playing";
                    presence.Assets.SmallImageText = "Playing";
                    break;
            }

                    // Guard against library null handling issues by ensuring assets and texts are always populated
                    presence.Assets ??= assets;
                    presence.Assets.LargeImageKey ??= "hyprism_logo";
                    presence.Assets.LargeImageText ??= "HyPrism Launcher";
                    presence.Assets.SmallImageKey ??= "hyprism_logo";
                    presence.Assets.SmallImageText ??= "HyPrism";

            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to set presence: {ex.Message}");
        }
    }

    public void ClearPresence()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to clear presence: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _client?.ClearPresence();
            _client?.Dispose();
            Logger.Info("Discord", "Discord RPC disposed");
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Error disposing Discord RPC: {ex.Message}");
        }
    }
}
