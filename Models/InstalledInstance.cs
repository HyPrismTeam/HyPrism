namespace HyPrism.Models;

/// <summary>
/// Represents the validation status of a game instance.
/// </summary>
public enum InstanceValidationStatus
{
    /// <summary>All required files are present and the instance is ready to launch.</summary>
    Valid,
    
    /// <summary>Some required files are missing (incomplete download or corrupted).</summary>
    Incomplete,
    
    /// <summary>The instance directory exists but no game files are present.</summary>
    NotInstalled,
    
    /// <summary>Critical files are missing or corrupted.</summary>
    Corrupted,
    
    /// <summary>Validation status has not been checked yet.</summary>
    Unknown
}

/// <summary>
/// Detailed information about what's missing or wrong with an instance.
/// </summary>
public class InstanceValidationDetails
{
    public bool HasExecutable { get; set; }
    public bool HasAssets { get; set; }
    public bool HasLibraries { get; set; }
    public bool HasConfig { get; set; }
    public List<string> MissingComponents { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class InstalledInstance
{
    public string Branch { get; set; } = "";
    public int Version { get; set; }
    public string Path { get; set; } = "";
    public bool HasUserData { get; set; }
    public long UserDataSize { get; set; }
    public long TotalSize { get; set; }
    
    /// <summary>
    /// Legacy property for backwards compatibility.
    /// Use ValidationStatus for more detailed information.
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Detailed validation status of the instance.
    /// </summary>
    public InstanceValidationStatus ValidationStatus { get; set; } = InstanceValidationStatus.Unknown;
    
    /// <summary>
    /// Detailed information about validation results.
    /// </summary>
    public InstanceValidationDetails? ValidationDetails { get; set; }
    
    public string? CustomName { get; set; }
}
