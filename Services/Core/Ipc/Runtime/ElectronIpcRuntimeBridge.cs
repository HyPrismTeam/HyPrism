using ElectronNET.API;
using ElectronNET.API.Entities;

namespace HyPrism.Services.Core.Ipc.Runtime;

public sealed class ElectronIpcRuntimeBridge : IIpcRuntimeBridge
{
    public IIpcMainBridge IpcMain { get; } = new ElectronIpcMainBridge();
    public IWindowManagerBridge WindowManager { get; } = new ElectronWindowManagerBridge();
    public IAppBridge App { get; } = new ElectronAppBridge();
    public IShellBridge Shell { get; } = new ElectronShellBridge();

    public void SendToMainWindow(string channel, string payload)
    {
        var win = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (win == null) return;
        Electron.IpcMain.Send(win, channel, payload);
    }

    private sealed class ElectronIpcMainBridge : IIpcMainBridge
    {
        public void On(string channel, Action<object?> handler)
        {
            Electron.IpcMain.On(channel, handler);
        }
    }

    private sealed class ElectronWindowManagerBridge : IWindowManagerBridge
    {
        public IReadOnlyList<IRuntimeWindow> BrowserWindows =>
            Electron.WindowManager.BrowserWindows
                .Select(w => (IRuntimeWindow)new ElectronRuntimeWindow(w))
                .ToList();
    }

    private sealed class ElectronRuntimeWindow(BrowserWindow window) : IRuntimeWindow
    {
        public void Minimize() => window.Minimize();
        public void Maximize() => window.Maximize();
        public void Unmaximize() => window.Unmaximize();
        public void Close() => window.Close();
        public Task<bool> IsMaximizedAsync() => window.IsMaximizedAsync();
    }

    private sealed class ElectronAppBridge : IAppBridge
    {
        public void Exit() => Electron.App.Exit();
    }

    private sealed class ElectronShellBridge : IShellBridge
    {
        public Task OpenExternalAsync(string url) => Electron.Shell.OpenExternalAsync(url);
        public Task OpenPathAsync(string path) => Electron.Shell.OpenPathAsync(path);
    }
}
