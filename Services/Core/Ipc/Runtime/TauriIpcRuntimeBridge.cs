using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Ipc.Runtime;

public sealed class TauriIpcRuntimeBridge : IIpcRuntimeBridge
{
    private const string ProtocolPrefix = "@@HYPRISM_IPC@@";

    private readonly ConcurrentDictionary<string, ConcurrentBag<Action<object?>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TauriRuntimeWindow _mainWindow = new();
    private readonly object _loopGate = new();
    private Task? _loopTask;

    public static TauriIpcRuntimeBridge Instance { get; } = new();

    public IIpcMainBridge IpcMain { get; }
    public IWindowManagerBridge WindowManager { get; }
    public IAppBridge App { get; } = new TauriAppBridge();
    public IShellBridge Shell { get; } = new TauriShellBridge();

    private TauriIpcRuntimeBridge()
    {
        IpcMain = new TauriIpcMainBridge(this);
        WindowManager = new TauriWindowManagerBridge(_mainWindow);
    }

    public Task Completion => _loopTask ?? Task.CompletedTask;

    public void Start()
    {
        lock (_loopGate)
        {
            if (_loopTask != null)
                return;

            _loopTask = Task.Run(ReadLoopAsync);
        }
    }

    public void SendToMainWindow(string channel, string payload)
    {
        var outbound = JsonSerializer.Serialize(new
        {
            type = "emit",
            channel,
            payload
        });

        Console.WriteLine($"{ProtocolPrefix}{outbound}");
    }

    private async Task ReadLoopAsync()
    {
        Logger.Info("IPC", "Tauri bridge runtime loop started");

        while (true)
        {
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync();
            }
            catch (Exception ex)
            {
                Logger.Warning("IPC", $"Tauri bridge stdin read failed: {ex.Message}");
                break;
            }

            if (line is null)
            {
                Logger.Info("IPC", "Tauri bridge stdin closed; stopping runtime loop");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            TauriInboundMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<TauriInboundMessage>(line);
            }
            catch
            {
                continue;
            }

            if (msg is null || string.IsNullOrWhiteSpace(msg.Channel))
                continue;

            if (_handlers.TryGetValue(msg.Channel, out var bag))
            {
                object? payload = msg.Payload;
                foreach (var handler in bag)
                {
                    try
                    {
                        handler(payload);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("IPC", $"Tauri handler error on '{msg.Channel}': {ex.Message}");
                    }
                }
            }
        }

        Logger.Info("IPC", "Tauri bridge runtime loop stopped");
    }

    private sealed class TauriIpcMainBridge(TauriIpcRuntimeBridge owner) : IIpcMainBridge
    {
        public void On(string channel, Action<object?> handler)
        {
            var bag = owner._handlers.GetOrAdd(channel, _ => []);
            bag.Add(handler);
        }
    }

    private sealed class TauriWindowManagerBridge(IRuntimeWindow mainWindow) : IWindowManagerBridge
    {
        public IReadOnlyList<IRuntimeWindow> BrowserWindows { get; } = [mainWindow];
    }

    private sealed class TauriRuntimeWindow : IRuntimeWindow
    {
        private bool _isMaximized;

        public void Minimize() => Logger.Debug("Window", "Tauri window minimize requested");

        public void Maximize()
        {
            _isMaximized = true;
            Logger.Debug("Window", "Tauri window maximize requested");
        }

        public void Unmaximize()
        {
            _isMaximized = false;
            Logger.Debug("Window", "Tauri window unmaximize requested");
        }

        public void Close()
        {
            Logger.Debug("Window", "Tauri window close requested");
            Environment.Exit(0);
        }

        public Task<bool> IsMaximizedAsync() => Task.FromResult(_isMaximized);
    }

    private sealed class TauriAppBridge : IAppBridge
    {
        public void Exit()
        {
            Logger.Info("IPC", "Tauri app exit requested");
            Environment.Exit(0);
        }
    }

    private sealed class TauriShellBridge : IShellBridge
    {
        public Task OpenExternalAsync(string url)
        {
            TryStart(url);
            return Task.CompletedTask;
        }

        public Task OpenPathAsync(string path)
        {
            TryStart(path);
            return Task.CompletedTask;
        }

        private static void TryStart(string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", target);
                    return;
                }

                Process.Start("xdg-open", target);
            }
            catch (Exception ex)
            {
                Logger.Warning("IPC", $"Failed to open target '{target}': {ex.Message}");
            }
        }
    }

    private sealed class TauriInboundMessage
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; set; }
    }
}
