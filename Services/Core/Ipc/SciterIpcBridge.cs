using System.Text.Json;
using EmptyFlow.SciterAPI;
using EmptyFlow.SciterAPI.Client;
using EmptyFlow.SciterAPI.Enums;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Ipc;

/// <summary>
/// Sciter-backed IPC bridge.
/// Registered as a singleton in DI; attached to the Sciter window by Program.cs
/// after window creation.
///
/// JS → C#:  document.getElementById('hyprism-ipc-bridge').xcall("hyprismCall", channel, jsonArgs)
///            — routed via element behavior 'hyprism-ipc' registered by this class.
/// C# → JS:  __hyprismReceive(channel, jsonData)  (global function defined in ipc.ts preload)
/// </summary>
public sealed class SciterIpcBridge : ISciterIpcBridge
{
    private SciterAPIHost? _host;
    private nint _mainWindow;

    // Strong reference to the window handler so the GC cannot collect it.
    private SciterIpcWindowHandler? _windowHandler;

    private readonly Dictionary<string, List<Action<object?>>> _handlers = [];
    private readonly Lock _lock = new();

    // -------------------------------------------------------------------------
    // Attachment — called once by Program.cs after CreateMainWindow()
    // -------------------------------------------------------------------------

    /// <summary>
    /// Bind the bridge to the Sciter host and main window.
    /// Registers a window-level event handler so that
    ///   Window.this.xcall("hyprismCall", channel, jsonArgs)
    /// from JS routes to ScriptMethodCall here.
    /// (Proven approach: see SciterAPI integration tests DocumentXCallHandler)
    /// </summary>
    public void Attach(SciterAPIHost host, nint mainWindow)
    {
        _host = host;
        _mainWindow = mainWindow;

        _windowHandler = new SciterIpcWindowHandler(host, this);
        host.AddWindowEventHandler(_windowHandler);

        Logger.Info("SciterBridge", "IPC bridge attached to Sciter window");
    }

    // -------------------------------------------------------------------------
    // ISciterIpcBridge
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void On(string channel, Action<object?> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(channel, out var list))
            {
                list = [];
                _handlers[channel] = list;
            }
            list.Add(handler);
        }
    }

    /// <inheritdoc/>
    public void Send(string channel, string json)
    {
        if (_host is null || _mainWindow == nint.Zero) return;

        // Escape the channel name for inline JS and call the receiver function
        var channelJson = JsonSerializer.Serialize(channel);
        // __hyprismReceive is defined in the preload/inline script injected into index.html
        var script = $"typeof __hyprismReceive === 'function' && __hyprismReceive({channelJson},{json})";

        try
        {
            _host.ExecuteWindowEval(_mainWindow, script, out _);
        }
        catch (Exception ex)
        {
            Logger.Warning("SciterBridge", $"Send failed on channel {channel}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void MinimizeWindow()
    {
        if (_host is null || _mainWindow == nint.Zero) return;
        try { _host.ExecuteWindowEval(_mainWindow, "Window.this.state = Window.WINDOW_MINIMIZED", out _); }
        catch (Exception ex) { Logger.Warning("SciterBridge", $"MinimizeWindow failed: {ex.Message}"); }
    }

    /// <inheritdoc/>
    public void ToggleMaximizeWindow()
    {
        if (_host is null || _mainWindow == nint.Zero) return;
        try
        {
            _host.ExecuteWindowEval(
                _mainWindow,
                "Window.this.state = (Window.this.state === Window.WINDOW_MAXIMIZED ? Window.WINDOW_NORMAL : Window.WINDOW_MAXIMIZED)",
                out _);
        }
        catch (Exception ex) { Logger.Warning("SciterBridge", $"ToggleMaximizeWindow failed: {ex.Message}"); }
    }

    /// <inheritdoc/>
    public void CloseWindow()
    {
        if (_host is null || _mainWindow == nint.Zero) return;
        try { _host.CloseWindow(_mainWindow); }
        catch (Exception ex) { Logger.Warning("SciterBridge", $"CloseWindow failed: {ex.Message}"); }
    }

    /// <inheritdoc/>
    public void EvalScript(string script)
    {
        if (_host is null || _mainWindow == nint.Zero) return;
        try { _host.ExecuteWindowEval(_mainWindow, script, out _); }
        catch (Exception ex) { Logger.Warning("SciterBridge", $"EvalScript failed: {ex.Message}"); }
    }

    // -------------------------------------------------------------------------
    // Internal dispatch — called by SciterIpcWindowHandler
    // -------------------------------------------------------------------------

    internal void DispatchCall(string channel, string argsJson)
    {
        List<Action<object?>>? handlers;
        lock (_lock)
        {
            _handlers.TryGetValue(channel, out handlers);
        }

        if (handlers is null) return;

        // Each handler receives the raw JSON string as the args object —
        // this mirrors how Electron.NET delivered args to IpcMain.On handlers.
        foreach (var h in handlers)
        {
            try { h(argsJson); }
            catch (Exception ex) { Logger.Error("SciterBridge", $"Handler for '{channel}' threw: {ex.Message}"); }
        }
    }
}

/// <summary>
/// Window-level event handler.
/// Receives Window.this.xcall("hyprismCall", channel, jsonArgs) from JS
/// and dispatches to <see cref="SciterIpcBridge.DispatchCall"/>.
/// Registered via host.AddWindowEventHandler() which subscribes to ALL events
/// (HandleAll) — no BeforeRegisterEvent override needed.
/// </summary>
internal sealed class SciterIpcWindowHandler : SciterEventHandler
{
    private readonly SciterIpcBridge _bridge;

    // Must use IntPtr.Zero + SciterEventHandlerMode.Window — matches the
    // pattern from SciterAPI integration test DocumentXCallHandler.
    public SciterIpcWindowHandler(SciterAPIHost host, SciterIpcBridge bridge)
        : base(nint.Zero, host, SciterEventHandlerMode.Window)
    {
        _bridge = bridge;
    }

    public override (SciterValue? value, bool handled) ScriptMethodCall(
        string name, IEnumerable<SciterValue> arguments)
    {
        if (name != "hyprismCall")
            return (null, false);

        var args = arguments.ToList();
        if (args.Count == 0)
            return (Host.CreateValue(false), true);

        var a0 = args[0];
        var channel = Host.GetValueString(ref a0);
        string jsonArgs;
        if (args.Count > 1) { var a1 = args[1]; jsonArgs = Host.GetValueString(ref a1); }
        else { jsonArgs = "null"; }

        // Dispatch asynchronously so the UI thread is never blocked
        _ = Task.Run(() => _bridge.DispatchCall(channel, jsonArgs));

        return (Host.CreateValue(true), true);
    }
}
