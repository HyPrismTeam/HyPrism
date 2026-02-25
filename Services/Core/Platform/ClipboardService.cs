using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Cross-platform clipboard service using native OS tools.
/// Windows: PowerShell Set-Clipboard / Get-Clipboard
/// macOS:   pbcopy / pbpaste
/// Linux:   xclip / xsel / wl-copy / wl-paste
/// </summary>
public class ClipboardService : IClipboardService
{
    /// <inheritdoc/>
    public async Task SetTextAsync(string text)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Set-Clipboard -Value @'\n{text}\n'@\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null) await p.WaitForExitAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "pbcopy",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    await p.StandardInput.WriteAsync(text);
                    p.StandardInput.Close();
                    await p.WaitForExitAsync();
                }
            }
            else
            {
                // Linux: try wl-copy (Wayland) then xclip (X11) then xsel
                await TryLinuxClipboardWrite(text, "wl-copy", "")
                    || await TryLinuxClipboardWrite(text, "xclip", "-selection clipboard")
                    || await TryLinuxClipboardWrite(text, "xsel", "--clipboard --input");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Clipboard", $"SetTextAsync failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetTextAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -NonInteractive -Command \"Get-Clipboard\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                if (p == null) return null;
                var text = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return text.TrimEnd('\r', '\n');
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "pbpaste",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                if (p == null) return null;
                var text = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return text;
            }
            else
            {
                return await TryLinuxClipboardRead("wl-paste", "--no-newline")
                    ?? await TryLinuxClipboardRead("xclip", "-selection clipboard -o")
                    ?? await TryLinuxClipboardRead("xsel", "--clipboard --output");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Clipboard", $"GetTextAsync failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> TryLinuxClipboardWrite(string text, string tool, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            });
            if (p == null) return false;
            await p.StandardInput.WriteAsync(text);
            p.StandardInput.Close();
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<string?> TryLinuxClipboardRead(string tool, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (p == null) return null;
            var text = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? text : null;
        }
        catch { return null; }
    }
}
