namespace HyPrism.Services.Core.Ipc.Runtime;

public interface IIpcRuntimeBridge
{
    IIpcMainBridge IpcMain { get; }
    IWindowManagerBridge WindowManager { get; }
    IAppBridge App { get; }
    IShellBridge Shell { get; }

    void SendToMainWindow(string channel, string payload);
}

public interface IIpcMainBridge
{
    void On(string channel, Action<object?> handler);
}

public interface IWindowManagerBridge
{
    IReadOnlyList<IRuntimeWindow> BrowserWindows { get; }
}

public interface IRuntimeWindow
{
    void Minimize();
    void Maximize();
    void Unmaximize();
    void Close();
    Task<bool> IsMaximizedAsync();
}

public interface IAppBridge
{
    void Exit();
}

public interface IShellBridge
{
    Task OpenExternalAsync(string url);
    Task OpenPathAsync(string path);
}
