using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

/// <summary>
/// Manages differential game updates by downloading and applying Butler PWR patches.
/// Extracted from the former monolithic GameSessionService.
/// </summary>
public class PatchManager : IPatchManager
{
    private readonly IVersionService _versionService;
    private readonly IButlerService _butlerService;
    private readonly IDownloadService _downloadService;
    private readonly IInstanceService _instanceService;
    private readonly IProgressNotificationService _progressService;
    private readonly HttpClient _httpClient;
    private readonly string _appDir;

    public PatchManager(
        IVersionService versionService,
        IButlerService butlerService,
        IDownloadService downloadService,
        IInstanceService instanceService,
        IProgressNotificationService progressService,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _versionService = versionService;
        _butlerService = butlerService;
        _downloadService = downloadService;
        _instanceService = instanceService;
        _progressService = progressService;
        _httpClient = httpClient;
        _appDir = appPath.AppDir;
    }

    public async Task ApplyDifferentialUpdateAsync(
        string versionPath,
        string branch,
        int installedVersion,
        int latestVersion,
        CancellationToken ct = default)
    {
        Logger.Info("Download", $"Differential update available: {installedVersion} -> {latestVersion}");
        _progressService.ReportDownloadProgress("update", 0, $"Updating game from v{installedVersion} to v{latestVersion}...", null, 0, 0);

        var patchesToApply = _versionService.GetPatchSequence(installedVersion, latestVersion);
        Logger.Info("Download", $"Patches to apply: {string.Join(" -> ", patchesToApply)}");

        for (int i = 0; i < patchesToApply.Count; i++)
        {
            int patchVersion = patchesToApply[i];
            ct.ThrowIfCancellationRequested();

            int baseProgress = (i * 90) / patchesToApply.Count;
            int progressPerPatch = 90 / patchesToApply.Count;

            _progressService.ReportDownloadProgress("update", baseProgress, $"Downloading patch {i + 1}/{patchesToApply.Count} (v{patchVersion})...", null, 0, 0);

            // Ensure Butler is installed
            await _butlerService.EnsureButlerInstalledAsync((_, _) => { });

            // Download the PWR patch
            var patchOs = UtilityService.GetOS();
            var patchArch = UtilityService.GetArch();
            var patchBranchType = UtilityService.NormalizeVersionType(branch);
            string patchUrl = $"https://game-patches.hytale.com/patches/{patchOs}/{patchArch}/{patchBranchType}/0/{patchVersion}.pwr";
            string patchPwrPath = Path.Combine(_appDir, "Cache", $"{branch}_patch_{patchVersion}.pwr");

            Directory.CreateDirectory(Path.GetDirectoryName(patchPwrPath)!);
            Logger.Info("Download", $"Downloading patch: {patchUrl}");

            await ValidatePatchFileAsync(patchUrl, ct);

            await _downloadService.DownloadFileAsync(patchUrl, patchPwrPath, (progress, downloaded, total) =>
            {
                int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                _progressService.ReportDownloadProgress("update", mappedProgress, $"Downloading patch {i + 1}/{patchesToApply.Count}... {progress}%", null, downloaded, total);
            }, ct);

            ct.ThrowIfCancellationRequested();

            int applyBaseProgress = baseProgress + (progressPerPatch / 2);
            _progressService.ReportDownloadProgress("update", applyBaseProgress, $"Applying patch {i + 1}/{patchesToApply.Count}...", null, 0, 0);

            await _butlerService.ApplyPwrAsync(patchPwrPath, versionPath, (progress, message) =>
            {
                int mappedProgress = applyBaseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                _progressService.ReportDownloadProgress("update", mappedProgress, message, null, 0, 0);
            }, ct);

            if (File.Exists(patchPwrPath))
            {
                try { File.Delete(patchPwrPath); } catch { /* Cleanup failure is non-fatal */ }
            }

            _instanceService.SaveLatestInfo(branch, patchVersion);
            Logger.Success("Download", $"Patch {patchVersion} applied successfully");
        }

        Logger.Success("Download", $"Differential update complete: now at v{latestVersion}");
    }

    private async Task ValidatePatchFileAsync(string patchUrl, CancellationToken ct)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, patchUrl);
            using var headResponse = await _httpClient.SendAsync(headRequest, ct);

            if (!headResponse.IsSuccessStatusCode)
            {
                Logger.Warning("Download", $"Patch file not found at {patchUrl}, skipping differential update");
                throw new Exception("Patch file not available");
            }

            var contentLength = headResponse.Content.Headers.ContentLength ?? 0;
            if (contentLength > 500 * 1024 * 1024)
            {
                Logger.Warning("Download", $"Patch file is too large ({contentLength / 1024 / 1024} MB), likely wrong version detection");
                throw new Exception("Patch file unexpectedly large - version detection may be incorrect");
            }
        }
        catch (HttpRequestException)
        {
            Logger.Warning("Download", $"Cannot check patch file at {patchUrl}, skipping differential update");
            throw new Exception("Cannot access patch file");
        }
    }
}
