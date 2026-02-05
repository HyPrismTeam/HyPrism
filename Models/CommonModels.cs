using System.Text.Json.Serialization;

namespace HyPrism.Models;

/// <summary>
/// Represents a username->UUID mapping for the frontend.
/// </summary>
public class UuidMapping
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
    public bool IsCurrent { get; set; } = false;
}

public class DownloadProgress
{
    public bool Success { get; set; }
    public int Progress { get; set; }
    public string? Error { get; set; }
    public bool Cancelled { get; set; }
}

public class ProgressUpdateMessage
{
    public string State { get; set; } = "unknown";
    public double Progress { get; set; }
    public string MessageKey { get; set; } = "common.loading";
    public object[]? Args { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
}

/// <summary>
/// Status of Rosetta 2 installation on macOS Apple Silicon.
/// </summary>
public class RosettaStatus
{
    public bool NeedsInstall { get; set; }
    public string Message { get; set; } = "";
    public string Command { get; set; } = "";
    public string? TutorialUrl { get; set; }
}

/// <summary>
/// Represents a cosmetic item from Assets.zip
/// </summary>
public class CosmeticItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Detailed information about an installed game instance.
/// </summary>
public class InstalledVersionInfo
{
    public int Version { get; set; }
    public string Branch { get; set; } = "";
    public string Path { get; set; } = "";
    public long UserDataSize { get; set; }
    public bool HasUserData { get; set; }
    /// <summary>
    /// When the instance was created (UTC).
    /// </summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>
    /// Total playtime in seconds.
    /// </summary>
    public int PlayTimeSeconds { get; set; }
    /// <summary>
    /// Formatted playtime string (HH:MM:SS or Dd HH:MM:SS).
    /// </summary>
    public string PlayTimeFormatted => FormatPlayTime(PlayTimeSeconds);
    
    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "0:00:00";
        
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.Days > 0)
            return $"{ts.Days}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}

/// <summary>
/// Version status response for latest instance.
/// </summary>
public class VersionStatus
{
    /// <summary>
    /// Status: "not_installed", "update_available", "current", "none", "error"
    /// </summary>
    public string Status { get; set; } = "none";
    
    /// <summary>
    /// Currently installed version number (0 for latest).
    /// </summary>
    public int InstalledVersion { get; set; }
    
    /// <summary>
    /// Latest available version number.
    /// </summary>
    public int LatestVersion { get; set; }
}
