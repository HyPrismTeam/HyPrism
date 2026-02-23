using HyPrism.Hosts;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Ipc.Runtime;

public static class IpcRuntimeBridgeFactory
{
    public static IIpcRuntimeBridge Create()
    {
        return RuntimeHostFactory.CurrentRuntimeId switch
        {
            "tauri" => TauriIpcRuntimeBridge.Instance,
            _ => new ElectronIpcRuntimeBridge(),
        };
    }
}
