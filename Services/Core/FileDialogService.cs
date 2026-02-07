using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyPrism.Services.Core;

/// <summary>
/// Handles native file dialog interactions across different operating systems.
/// Provides cross-platform file browsing capabilities using OS-specific dialogs.
/// </summary>
public class FileDialogService : IFileDialogService
{
    /// <summary>
    /// Opens a folder browser dialog and returns the selected path.
    /// </summary>
    public async Task<string?> BrowseFolderAsync(string? initialPath = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = $@"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; ";
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    script += $@"$dialog.SelectedPath = '{initialPath.Replace("'", "''")}'; ";
                script += @"if ($dialog.ShowDialog() -eq 'OK') { $dialog.SelectedPath }";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var initialDir = !string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath) 
                    ? $"default location \"{initialPath}\"" 
                    : "";
                    
                var script = $@"tell application ""Finder""
                    activate
                    set theFolder to choose folder with prompt ""Select Folder"" {initialDir}
                    return POSIX path of theFolder
                end tell";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else
            {
                // Linux - use zenity
                var args = "--file-selection --directory --title=\"Select Folder\"";
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    args += $" --filename=\"{initialPath}/\"";
                    
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse folder: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Opens a native file dialog to browse for mod files.
    /// Supports multiple file selection across Windows, macOS, and Linux.
    /// </summary>
    /// <returns>Array of selected file paths, or empty array if cancelled</returns>
    public async Task<string[]> BrowseModFilesAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseFilesMacOSAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseFilesWindowsAsync();
            }
            else
            {
                return await BrowseFilesLinuxAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse files: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// macOS file picker using AppleScript.
    /// </summary>
    private static async Task<string[]> BrowseFilesMacOSAsync()
    {
        var script = @"tell application ""Finder""
            activate
            set theFiles to choose file with prompt ""Select Mod Files"" of type {""jar"", ""zip"", ""hmod"", ""litemod"", ""json""} with multiple selections allowed
            set filePaths to """"
            repeat with aFile in theFiles
                set filePaths to filePaths & POSIX path of aFile & ""\n""
            end repeat
            return filePaths
        end tell";
        
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Windows file picker using PowerShell.
    /// </summary>
    private static async Task<string[]> BrowseFilesWindowsAsync()
    {
        var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Mod Files (*.jar;*.zip;*.hmod;*.litemod;*.json)|*.jar;*.zip;*.hmod;*.litemod;*.json|All Files (*.*)|*.*'; $dialog.Multiselect = $true; $dialog.Title = 'Select Mod Files'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileNames -join ""`n"" }";
        
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Linux file picker using zenity.
    /// </summary>
    private static async Task<string[]> BrowseFilesLinuxAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = "--file-selection --multiple --title=\"Select Mod Files\" --file-filter=\"Mod Files | *.jar *.zip *.hmod *.litemod *.json\" --separator=\"\\n\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }
}
