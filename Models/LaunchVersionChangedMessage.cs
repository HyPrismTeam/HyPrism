namespace HyPrism.Models;

/// <summary>
/// Message sent when the launch version or branch is changed in settings.
/// Used to synchronize GameControlViewModel with SettingsViewModel.
/// </summary>
public class LaunchVersionChangedMessage
{
    public string Branch { get; }
    public int Version { get; }
    
    public LaunchVersionChangedMessage(string branch, int version)
    {
        Branch = branch;
        Version = version;
    }
}
