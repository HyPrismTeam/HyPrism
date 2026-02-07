using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyPrism.Services.Core;

public class FileService : IFileService
{
    private readonly string _appDir;

    public FileService(AppPathConfiguration appPath)
    {
        _appDir = appPath.AppDir;
    }

    public bool OpenAppFolder() => OpenFolderInExplorer(_appDir);

    public bool OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return false;
        return OpenFolderInExplorer(path);
    }

    private bool OpenFolderInExplorer(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            else
            {
                Process.Start("xdg-open", $"\"{path}\"")?.Dispose();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Files", $"Failed to open folder '{path}': {ex.Message}");
            return false;
        }
    }
}
