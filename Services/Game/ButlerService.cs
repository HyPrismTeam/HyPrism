using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using HyPrism.Services.Core;

namespace HyPrism.Services.Game;

public class ButlerService
{
    private const string ButlerVersion = "15.21.0";
    private const string BrothUrlTemplate = "https://broth.itch.zone/butler/{0}-{1}/LATEST/archive/default";
    
    private readonly string _butlerDir;
    private readonly string _cacheDir;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public ButlerService(string appDir)
    {
        _butlerDir = Path.Combine(appDir, "Butler");
        _cacheDir = Path.Combine(appDir, "Cache");
        Directory.CreateDirectory(_butlerDir);
        Directory.CreateDirectory(_cacheDir);
    }

    public string GetButlerPath()
    {
        string name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "butler.exe" : "butler";
        return Path.Combine(_butlerDir, name);
    }

    public bool IsButlerInstalled()
    {
        string path = GetButlerPath();
        return File.Exists(path);
    }

    public async Task<string> EnsureButlerInstalledAsync(Action<int, string>? progressCallback = null)
    {
        // Recreate directories if they were removed externally
        Directory.CreateDirectory(_butlerDir);
        Directory.CreateDirectory(_cacheDir);

        string butlerPath = GetButlerPath();
        
        // Check if butler exists AND works
        if (File.Exists(butlerPath))
        {
            // Verify butler actually works
            if (await VerifyButlerWorksAsync(butlerPath))
            {
                progressCallback?.Invoke(100, "Butler ready");
                return butlerPath;
            }
            else
            {
                // Butler exists but doesn't work - delete and re-download
                Logger.Warning("Butler", "Butler exists but is not working, re-downloading...");
                try
                {
                    File.Delete(butlerPath);
                    // Also delete the entire butler directory to clean up
                    if (Directory.Exists(_butlerDir))
                    {
                        Directory.Delete(_butlerDir, true);
                        Directory.CreateDirectory(_butlerDir);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Butler", $"Failed to clean up: {ex.Message}");
                }
            }
        }

        progressCallback?.Invoke(0, "Downloading Butler...");

        // Determine OS and architecture
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();

        // Butler only provides darwin-amd64 (no arm64), so on macOS always use amd64
        // which runs through Rosetta 2 on Apple Silicon
        if (osName == "darwin")
        {
            arch = "amd64";
        }

        string url = string.Format(BrothUrlTemplate, osName, arch);
        Logger.Info("Butler", $"Downloading from: {url}");

        string archivePath = Path.Combine(_cacheDir, "butler.zip");

        try
        {
            // Download butler archive
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    int progress = (int)((totalRead * 80) / totalBytes); // 0-80% for download
                    progressCallback?.Invoke(progress, "Downloading Butler...");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Download failed: {ex.Message}");
            throw new Exception($"Failed to download Butler: {ex.Message}");
        }

        progressCallback?.Invoke(85, "Extracting Butler...");

        try
        {
            // Extract butler
            ZipFile.ExtractToDirectory(archivePath, _butlerDir, overwriteFiles: true);
            File.Delete(archivePath);
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Extraction failed: {ex.Message}");
            throw new Exception($"Failed to extract Butler: {ex.Message}");
        }

        // Make executable on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{butlerPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmod?.WaitForExit();
            }
            catch { }
        }

        progressCallback?.Invoke(95, "Verifying Butler...");

        // Verify butler works
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = butlerPath,
                Arguments = "version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                Logger.Success("Butler", $"Installed: {output.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Verification failed: {ex.Message}");
            throw new Exception($"Butler verification failed: {ex.Message}");
        }

        progressCallback?.Invoke(100, "Butler ready");
        return butlerPath;
    }

    private async Task<bool> VerifyButlerWorksAsync(string butlerPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = butlerPath,
                Arguments = "version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    return process.ExitCode == 0;
                }
                catch
                {
                    process.Kill();
                    return false;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyPwrAsync(string pwrFile, string targetDir, Action<int, string>? progressCallback = null, CancellationToken externalCancellationToken = default)
    {
        string butlerPath = await EnsureButlerInstalledAsync(progressCallback);
        string stagingDir = Path.Combine(targetDir, "staging-temp");

        progressCallback?.Invoke(5, "Preparing installation...");

        // Clean staging directory
        CleanStagingDirectory(targetDir);

        // Create directories
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(stagingDir);

        progressCallback?.Invoke(10, "Installing game...");

        Logger.Info("Butler", $"Applying PWR: {pwrFile} -> {targetDir}");

        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"apply --staging-dir \"{stagingDir}\" --save-interval=60 \"{pwrFile}\" \"{targetDir}\""
            : $"apply --staging-dir \"{stagingDir}\" \"{pwrFile}\" \"{targetDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = butlerPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = targetDir
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new Exception("Failed to start Butler process");
        }

        // Use a timeout for the entire operation (8 minutes max) combined with external cancellation
        using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(8));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, externalCancellationToken);
        var cts = linkedCts;
        
        // Track progress with a simulated progress if butler doesn't report
        int lastProgress = 10;
        var progressTimer = new System.Timers.Timer(2000); // Update every 2 seconds
        progressTimer.Elapsed += (s, e) =>
        {
            // Simulate progress if no updates from butler (max 90%)
            if (lastProgress < 90)
            {
                lastProgress = Math.Min(lastProgress + 2, 90);
                progressCallback?.Invoke(lastProgress, "Installing game...");
            }
        };
        progressTimer.Start();

        string output = "";
        string error = "";

        try
        {
            // Read output and error concurrently but with proper handling
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            var outputTask = Task.Run(async () =>
            {
                try
                {
                    char[] buffer = new char[1024];
                    while (true)
                    {
                        int read = await process.StandardOutput.ReadAsync(buffer, cts.Token);
                        if (read == 0) break;
                        
                        string chunk = new string(buffer, 0, read);
                        outputBuilder.Append(chunk);
                        
                        // Parse progress from butler output if available
                        if (chunk.Contains("%"))
                        {
                            // Butler outputs progress like "patching 45.2%"
                            var match = System.Text.RegularExpressions.Regex.Match(chunk, @"(\d+(?:\.\d+)?)%");
                            if (match.Success && double.TryParse(match.Groups[1].Value, 
                                System.Globalization.NumberStyles.Any, 
                                System.Globalization.CultureInfo.InvariantCulture, 
                                out double pct))
                            {
                                // Map butler progress (0-100) to our range (10-95)
                                int mappedProgress = 10 + (int)(pct * 0.85);
                                if (mappedProgress > lastProgress)
                                {
                                    lastProgress = mappedProgress;
                                    progressCallback?.Invoke(mappedProgress, "Installing game...");
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) 
                { 
                    Logger.Warning("Butler", $"Output read error: {ex.Message}");
                }
            }, cts.Token);

            var errorTask = Task.Run(async () =>
            {
                try
                {
                    errorBuilder.Append(await process.StandardError.ReadToEndAsync(cts.Token));
                }
                catch (OperationCanceledException) { }
                catch { }
            }, cts.Token);

            // Wait for process to exit with timeout
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (externalCancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Butler", "Butler process cancelled by user");
                    try { process.Kill(); } catch { }
                    CleanStagingDirectory(targetDir);
                    throw new OperationCanceledException("Download cancelled by user.");
                }
                else
                {
                    Logger.Error("Butler", "Butler process timed out after 8 minutes");
                    try { process.Kill(); } catch { }
                    throw new Exception("Installation timed out. Please try again.");
                }
            }

            // Give output tasks a short time to finish after process exits
            try
            {
                await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                Logger.Warning("Butler", "Output tasks did not finish in time");
            }

            output = outputBuilder.ToString();
            error = errorBuilder.ToString();
        }
        finally
        {
            progressTimer.Stop();
            progressTimer.Dispose();
        }

        if (process.ExitCode != 0)
        {
            Logger.Error("Butler", $"Error output: {error}");
            CleanStagingDirectory(targetDir);
            throw new Exception($"Butler apply failed (exit code {process.ExitCode}): {error}");
        }

        Logger.Debug("Butler", $"Output: {output}");

        // Clean up staging directory
        CleanStagingDirectory(targetDir);

        progressCallback?.Invoke(98, "Setting permissions...");

        // Set executable permissions on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string clientPath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(targetDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient")
                : Path.Combine(targetDir, "Client", "HytaleClient");

            if (File.Exists(clientPath))
            {
                try
                {
                    var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{clientPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    chmod?.WaitForExit();
                }
                catch { }
            }
        }

        progressCallback?.Invoke(100, "Installation complete");
        Logger.Success("Butler", "Installation complete");
    }

    private void CleanStagingDirectory(string gameDir)
    {
        string stagingDir = Path.Combine(gameDir, "staging-temp");

        try
        {
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }
        }
        catch
        {
            // On Windows, try to remove files individually if directory removal fails
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Directory.Exists(stagingDir))
            {
                foreach (var file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
                try { Directory.Delete(stagingDir, true); } catch { }
            }
        }

        // Also clean any .tmp files in game directory that butler might have left
        if (Directory.Exists(gameDir))
        {
            foreach (var file in Directory.GetFiles(gameDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp") || name.StartsWith("sf-"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }
}