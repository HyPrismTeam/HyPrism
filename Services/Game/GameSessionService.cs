using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Orchestrates the download/update/launch workflow.
/// Delegates to IPatchManager for differential updates and IGameLauncher for game startup.
/// Reduced from ~1000 lines to ~300 through decomposition.
/// </summary>
public class GameSessionService : IGameSessionService
{
    private readonly IConfigService _configService;
    private readonly IInstanceService _instanceService;
    private readonly IVersionService _versionService;
    private readonly ILaunchService _launchService;
    private readonly IButlerService _butlerService;
    private readonly IDownloadService _downloadService;
    private readonly IProgressNotificationService _progressService;
    private readonly IPatchManager _patchManager;
    private readonly IGameLauncher _gameLauncher;
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    private volatile bool _cancelRequested;
    private CancellationTokenSource? _downloadCts;
    private readonly object _ctsLock = new();

    public GameSessionService(
        IConfigService configService,
        IInstanceService instanceService,
        IVersionService versionService,
        ILaunchService launchService,
        IButlerService butlerService,
        IDownloadService downloadService,
        IProgressNotificationService progressService,
        IPatchManager patchManager,
        IGameLauncher gameLauncher,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _configService = configService;
        _instanceService = instanceService;
        _versionService = versionService;
        _launchService = launchService;
        _butlerService = butlerService;
        _downloadService = downloadService;
        _progressService = progressService;
        _patchManager = patchManager;
        _gameLauncher = gameLauncher;
        _httpClient = httpClient;
        _appDir = appPath.AppDir;
    }

    private Config _config => _configService.Configuration;

    public async Task<DownloadProgress> DownloadAndLaunchAsync(Func<bool>? launchAfterDownloadProvider = null)
    {
        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            if (_cancelRequested)
            {
                _cancelRequested = false;
                return new DownloadProgress { Cancelled = true };
            }
            cts = new CancellationTokenSource();
            _downloadCts = cts;
        }

        try
        {
            _progressService.ReportDownloadProgress("preparing", 0, "launch.detail.preparing_session", null, 0, 0);

            string branch = UtilityService.NormalizeVersionType(_config.VersionType);
            var versions = await _versionService.GetVersionListAsync(branch, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            if (versions.Count == 0)
                return new DownloadProgress { Error = "No versions available for this branch" };

            bool isLatestInstance = _config.SelectedVersion == 0;
            int targetVersion = _config.SelectedVersion > 0 ? _config.SelectedVersion : versions[0];
            if (!versions.Contains(targetVersion))
                targetVersion = versions[0];

            string versionPath = _instanceService.ResolveInstancePath(branch, isLatestInstance ? 0 : targetVersion, preferExisting: true);
            Directory.CreateDirectory(versionPath);

            bool gameIsInstalled = _instanceService.IsClientPresent(versionPath);

            Logger.Info("Download", $"=== INSTALL CHECK ===", false);
            Logger.Info("Download", $"Version path: {versionPath}", false);
            Logger.Info("Download", $"Is latest instance: {isLatestInstance}", false);
            Logger.Info("Download", $"Target version: {targetVersion}", false);
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}", false);

            if (gameIsInstalled)
            {
                return await HandleInstalledGameAsync(versionPath, branch, isLatestInstance, versions, cts.Token);
            }

            return await HandleFreshInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Operation cancelled");
            return new DownloadProgress { Error = "Cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Fatal error: {ex.Message}");
            Logger.Error("Download", ex.ToString());
            _progressService.ReportError("fatal", "Fatal error", ex.ToString());
            return new DownloadProgress { Error = $"Fatal error: {ex.Message}" };
        }
        finally
        {
            lock (_ctsLock)
            {
                _downloadCts = null;
                _cancelRequested = false;
            }
            cts.Dispose();
        }
    }

    public void CancelDownload()
    {
        _cancelRequested = true;
        lock (_ctsLock)
        {
            _downloadCts?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_ctsLock)
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private async Task<DownloadProgress> HandleInstalledGameAsync(
        string versionPath, string branch, bool isLatestInstance,
        List<int> versions, CancellationToken ct)
    {
        Logger.Success("Download", "Game is already installed");

        // Check for differential updates (only for latest instance)
        if (isLatestInstance)
        {
            await TryApplyDifferentialUpdateAsync(versionPath, branch, versions, ct);
        }

        await EnsureRuntimeDependenciesAsync(ct);

        _progressService.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);
        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, ct);
            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task TryApplyDifferentialUpdateAsync(
        string versionPath, string branch, List<int> versions, CancellationToken ct)
    {
        var info = _instanceService.LoadLatestInfo(branch);
        int installedVersion = info?.Version ?? 0;
        int latestVersion = versions[0];

        // Detect installed version from cache if no latest.json
        if (installedVersion == 0)
        {
            installedVersion = DetectInstalledVersion(versionPath, branch);
        }

        Logger.Info("Download", $"Installed version: {installedVersion}, Latest version: {latestVersion}", false);

        if (installedVersion > 0 && installedVersion < latestVersion)
        {
            try
            {
                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, installedVersion, latestVersion, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Differential update failed: {ex.Message}");
                Logger.Warning("Download", "Keeping current version, user can try UPDATE again later");
            }
        }
        else if (installedVersion >= latestVersion)
        {
            Logger.Info("Download", "Already at latest version, no update needed", false);
            _instanceService.SaveLatestInfo(branch, latestVersion);
        }
    }

    private int DetectInstalledVersion(string versionPath, string branch)
    {
        var receiptPath = Path.Combine(versionPath, ".itch", "receipt.json.gz");
        if (!File.Exists(receiptPath)) return 0;

        var cacheDir = Path.Combine(_appDir, "Cache");
        if (!Directory.Exists(cacheDir)) return 0;

        var pwrFiles = Directory.GetFiles(cacheDir, $"{branch}_patch_*.pwr")
            .Concat(Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .SelectMany(n =>
            {
                var parts = n.Split('_');
                var vs = new List<int>();
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out var v) && v > 0)
                        vs.Add(v);
                }
                return vs;
            })
            .OrderByDescending(v => v)
            .ToList();

        if (pwrFiles.Count > 0)
        {
            int detected = pwrFiles[0];
            Logger.Info("Download", $"Detected installed version from cache: v{detected}", false);
            _instanceService.SaveLatestInfo(branch, detected);
            return detected;
        }

        Logger.Info("Download", "Butler receipt exists but no version info, launching as-is", false);
        return 0;
    }

    private async Task<DownloadProgress> HandleFreshInstallAsync(
        string versionPath, string branch, bool isLatestInstance,
        int targetVersion, Func<bool>? launchAfterDownloadProvider, CancellationToken ct)
    {
        Logger.Info("Download", "Game not installed, starting download...");
        _progressService.ReportDownloadProgress("download", 0, "launch.detail.preparing_download", null, 0, 0);

        try
        {
            await _butlerService.EnsureButlerInstalledAsync((progress, message) =>
            {
                int mappedProgress = (int)(progress * 0.05);
                _progressService.ReportDownloadProgress("download", mappedProgress, message, null, 0, 0);
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Butler install failed: {ex.Message}");
            return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
        }

        ct.ThrowIfCancellationRequested();

        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();
        string apiVersionType = UtilityService.NormalizeVersionType(branch);
        string downloadUrl = $"https://game-patches.hytale.com/patches/{osName}/{arch}/{apiVersionType}/0/{targetVersion}.pwr";
        string pwrPath = Path.Combine(_appDir, "Cache", $"{branch}_{(isLatestInstance ? "latest" : "version")}_{targetVersion}.pwr");

        Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);

        await DownloadPwrWithCachingAsync(downloadUrl, pwrPath, ct);

        // Extract PWR
        _progressService.ReportDownloadProgress("install", 65, "launch.detail.installing_butler_pwr", null, 0, 0);

        try
        {
            await _butlerService.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
            {
                int mappedProgress = 65 + (int)(progress * 0.20);
                _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
            }, ct);

            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
            return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
        }

        if (isLatestInstance)
            _instanceService.SaveLatestInfo(branch, targetVersion);

        _progressService.ReportDownloadProgress("complete", 95, "launch.detail.download_complete", null, 0, 0);

        await EnsureRuntimeDependenciesAsync(ct);

        ct.ThrowIfCancellationRequested();

        var shouldLaunchAfterDownload = launchAfterDownloadProvider?.Invoke() ?? true;
        if (!shouldLaunchAfterDownload)
        {
            _progressService.ReportDownloadProgress("complete", 100, "launch.detail.done", null, 0, 0);
            return new DownloadProgress { Success = true, Progress = 100 };
        }

        _progressService.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);

        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, ct);

            // Cleanup cache after successful launch
            if (File.Exists(pwrPath))
            {
                try { File.Delete(pwrPath); } catch { /* Cleanup failure is non-fatal */ }
            }

            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task DownloadPwrWithCachingAsync(string downloadUrl, string pwrPath, CancellationToken ct)
    {
        bool needDownload = true;
        long remoteSize = -1;

        try { remoteSize = await _downloadService.GetFileSizeAsync(downloadUrl, ct); }
        catch { /* Proceed to download anyway */ }

        if (File.Exists(pwrPath))
        {
            if (remoteSize > 0)
            {
                long localSize = new FileInfo(pwrPath).Length;
                if (localSize == remoteSize)
                {
                    Logger.Info("Download", "Using cached PWR file.");
                    needDownload = false;
                }
                else
                {
                    Logger.Warning("Download", $"Cached file size mismatch ({localSize} vs {remoteSize}). Deleting.");
                    try { File.Delete(pwrPath); } catch { }
                }
            }
            else
            {
                Logger.Info("Download", "Cannot verify remote size, using valid local cache entry.");
                needDownload = false;
            }
        }

        if (needDownload)
        {
            Logger.Info("Download", $"Downloading: {downloadUrl}");
            string partPath = pwrPath + ".part";

            await _downloadService.DownloadFileAsync(downloadUrl, partPath, (progress, downloaded, total) =>
            {
                int mappedProgress = 5 + (int)(progress * 0.60);
                _progressService.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_generic", [progress], downloaded, total);
            }, ct);

            if (File.Exists(partPath))
                File.Move(partPath, pwrPath, true);
        }
        else
        {
            _progressService.ReportDownloadProgress("download", 65, "launch.detail.using_cached_installer", null, 0, 0);
        }
    }

    private async Task EnsureRuntimeDependenciesAsync(CancellationToken ct)
    {
        // VC++ Redist check (Windows only)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _progressService.ReportDownloadProgress("install", 94, "launch.detail.vc_redist", null, 0, 0);
            try
            {
                await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 94 + (int)(progress * 0.02);
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
            }
        }

        // JRE check
        string jrePath = _launchService.GetJavaPath();
        if (!File.Exists(jrePath))
        {
            Logger.Info("Download", "JRE missing, installing...");
            _progressService.ReportDownloadProgress("install", 96, "launch.detail.java_install", null, 0, 0);
            await _launchService.EnsureJREInstalledAsync((progress, message) =>
            {
                int mappedProgress = 96 + (int)(progress * 0.03);
                _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
            });
        }
    }
}
