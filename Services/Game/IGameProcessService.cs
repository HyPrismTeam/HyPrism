using System.Diagnostics;

namespace HyPrism.Services.Game;

public interface IGameProcessService
{
    void SetGameProcess(Process? p);
    Process? GetGameProcess();
    bool IsGameRunning();
    bool CheckForRunningGame();
    bool ExitGame();
}
