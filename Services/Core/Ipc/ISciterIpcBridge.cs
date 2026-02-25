namespace HyPrism.Services.Core.Ipc;

/// <summary>
/// Abstraction for the IPC transport layer.
/// In the Sciter build this is backed by <see cref="SciterIpcBridge"/>.
/// </summary>
public interface ISciterIpcBridge
{
    /// <summary>Register a handler for a named channel (fire-and-forget or invoke).</summary>
    void On(string channel, Action<object?> handler);

    /// <summary>Push a pre-serialised JSON payload to the renderer on the named channel.</summary>
    void Send(string channel, string json);

    // ---- Window management helpers ----

    void MinimizeWindow();
    void ToggleMaximizeWindow();
    void CloseWindow();

    /// <summary>Evaluate arbitrary script in the renderer context.</summary>
    void EvalScript(string script);
}
