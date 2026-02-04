using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HyPrism.Services.Core;

/// <summary>
/// Service responsible for opening URLs in the default browser.
/// </summary>
public class BrowserService
{
    /// <summary>
    /// Opens the specified URL in the default browser.
    /// </summary>
    public bool OpenURL(string url)
    {
        try
        {
            Logger.Info("Browser", $"Opening URL: {url}");
            
            // Validate URL
            if (string.IsNullOrWhiteSpace(url) || (!url.StartsWith("http://") && !url.StartsWith("https://")))
            {
                Logger.Warning("Browser", $"Invalid URL: {url}");
                return false;
            }

            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows
                psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS
                psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = false
                };
            }
            else
            {
                // Linux
                psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }

            var process = Process.Start(psi);
            
            // On Linux, xdg-open might spawn a browser that emits noise to stderr (e.g. sandbox warnings).
            // We redirect and discard these streams to keep the application logs clean.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && process != null)
            {
                _ = Task.Run(() => process.StandardOutput.ReadToEndAsync());
                _ = Task.Run(() => process.StandardError.ReadToEndAsync());
            }

            Logger.Success("Browser", $"Opened URL: {url}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Browser", $"Failed to open URL: {ex.Message}");
            return false;
        }
    }
}
