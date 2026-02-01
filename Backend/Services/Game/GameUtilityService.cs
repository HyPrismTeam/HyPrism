using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HyPrism.Backend.Models;

namespace HyPrism.Backend.Services.Game;

/// <summary>
/// Утилиты для управления папками игры, процессами и файловой системой.
/// </summary>
public class GameUtilityService
{
    private readonly string _appDir;
    private readonly Config _config;
    private readonly Func<string, string> _normalizeVersionType;
    private readonly Func<string, int, bool, string> _resolveInstancePath;
    private readonly Func<string, string> _getLatestInfoPath;
    private readonly Func<string, int, string> _getInstancePath;
    private readonly Func<string> _getProfilesFolder;
    private readonly Func<string, string> _sanitizeFileName;
    private readonly Func<Process?> _getGameProcess;
    private readonly Action<Process?> _setGameProcess;

    public GameUtilityService(
        string appDir,
        Config config,
        Func<string, string> normalizeVersionType,
        Func<string, int, bool, string> resolveInstancePath,
        Func<string, string> getLatestInfoPath,
        Func<string, int, string> getInstancePath,
        Func<string> getProfilesFolder,
        Func<string, string> sanitizeFileName,
        Func<Process?> getGameProcess,
        Action<Process?> setGameProcess)
    {
        _appDir = appDir;
        _config = config;
        _normalizeVersionType = normalizeVersionType;
        _resolveInstancePath = resolveInstancePath;
        _getLatestInfoPath = getLatestInfoPath;
        _getInstancePath = getInstancePath;
        _getProfilesFolder = getProfilesFolder;
        _sanitizeFileName = sanitizeFileName;
        _getGameProcess = getGameProcess;
        _setGameProcess = setGameProcess;
    }

    #region Game Process Management

    /// <summary>
    /// Checks if the game process is currently running.
    /// </summary>
    public bool IsGameRunning()
    {
        var gameProcess = _getGameProcess();
        return gameProcess != null && !gameProcess.HasExited;
    }

    /// <summary>
    /// Forces the game process to exit.
    /// </summary>
    public bool ExitGame()
    {
        var gameProcess = _getGameProcess();
        if (gameProcess != null && !gameProcess.HasExited)
        {
            gameProcess.Kill();
            _setGameProcess(null);
            return true;
        }
        return false;
    }

    #endregion

    #region Game Version Management

    /// <summary>
    /// Deletes a game instance by branch and version number.
    /// Also removes latest.json for latest instances (version 0).
    /// </summary>
    public bool DeleteGame(string branch, int versionNumber)
    {
        try
        {
            string normalizedBranch = _normalizeVersionType(branch);
            string versionPath = _resolveInstancePath(normalizedBranch, versionNumber, true);
            
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
            }
            
            if (versionNumber == 0)
            {
                var infoPath = _getLatestInfoPath(normalizedBranch);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Folder Operations

    /// <summary>
    /// Opens the main app directory in the system file manager.
    /// </summary>
    public bool OpenFolder()
    {
        return OpenFolderInExplorer(_appDir);
    }

    /// <summary>
    /// Opens the current active profile's folder in the system file manager.
    /// Creates the folder if it doesn't exist.
    /// </summary>
    public bool OpenCurrentProfileFolder()
    {
        try
        {
            if (_config.ActiveProfileIndex < 0 || _config.Profiles == null || 
                _config.ActiveProfileIndex >= _config.Profiles.Count)
            {
                Logger.Warning("Profile", "No active profile to open folder for");
                return false;
            }
            
            var profile = _config.Profiles[_config.ActiveProfileIndex];
            var safeName = _sanitizeFileName(profile.Name);
            var profileDir = Path.Combine(_getProfilesFolder(), safeName);
            
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
            }
            
            if (OpenFolderInExplorer(profileDir))
            {
                Logger.Success("Profile", $"Opened profile folder: {profileDir}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to open profile folder: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a specific game instance folder in the system file manager.
    /// </summary>
    public bool OpenInstanceFolder(string branch, int version)
    {
        try
        {
            var instancePath = _getInstancePath(branch, version);
            if (!Directory.Exists(instancePath))
            {
                Logger.Warning("Files", $"Instance folder does not exist: {instancePath}");
                return false;
            }
            
            if (OpenFolderInExplorer(instancePath))
            {
                Logger.Success("Files", $"Opened instance folder: {instancePath}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Files", $"Failed to open instance folder: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a folder in the system's file explorer (cross-platform).
    /// </summary>
    private bool OpenFolderInExplorer(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start("xdg-open", $"\"{path}\"");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Files", $"Failed to open folder '{path}': {ex.Message}");
            return false;
        }
    }

    #endregion
}
