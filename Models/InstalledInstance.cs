namespace HyPrism.Models;

public class InstalledInstance
{
    public string Branch { get; set; } = "";
    public int Version { get; set; }
    public string Path { get; set; } = "";
    public bool HasUserData { get; set; }
    public long UserDataSize { get; set; }
    public long TotalSize { get; set; }
}
