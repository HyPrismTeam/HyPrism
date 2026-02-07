using HyPrism.Models;

namespace HyPrism.Services.Core;

public interface IConfigService
{
    Config Configuration { get; }
    void SaveConfig();
    void ResetConfig();
    Task<string?> SetInstanceDirectoryAsync(string path);
}
