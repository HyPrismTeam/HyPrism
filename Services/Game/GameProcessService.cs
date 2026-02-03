using System.Diagnostics;

namespace HyPrism.Services.Game;

public class GameProcessService
{
    private Process? _gameProcess;

    public void SetGameProcess(Process? p) => _gameProcess = p;
    public Process? GetGameProcess() => _gameProcess;

    public bool IsGameRunning()
    {
        var gameProcess = _gameProcess;
        return gameProcess != null && !gameProcess.HasExited;
    }

    public bool ExitGame()
    {
        var gameProcess = _gameProcess;
        if (gameProcess != null && !gameProcess.HasExited)
        {
            gameProcess.Kill();
            SetGameProcess(null);
            return true;
        }
        return false;
    }
}
