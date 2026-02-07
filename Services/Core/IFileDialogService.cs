namespace HyPrism.Services.Core;

public interface IFileDialogService
{
    Task<string?> BrowseFolderAsync(string? initialPath = null);
    Task<string[]> BrowseModFilesAsync();
}
