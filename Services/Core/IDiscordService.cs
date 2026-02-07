namespace HyPrism.Services.Core;

public interface IDiscordService : IDisposable
{
    void Initialize();
    void SetPresence(DiscordService.PresenceState state, string? details = null, int? progress = null);
    void ClearPresence();
}
