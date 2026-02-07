using HyPrism.Models;

namespace HyPrism.Services.Core;

public interface IProgressNotificationService
{
    event Action<ProgressUpdateMessage>? DownloadProgressChanged;
    event Action<string, int>? GameStateChanged;
    event Action<string, string, string?>? ErrorOccurred;
    
    void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0);
    void ReportGameStateChanged(string state, int? exitCode = null);
    void ReportError(string type, string message, string? technical = null);
}
