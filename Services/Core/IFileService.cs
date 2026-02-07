namespace HyPrism.Services.Core;

public interface IFileService
{
    bool OpenAppFolder();
    bool OpenFolder(string path);
}
