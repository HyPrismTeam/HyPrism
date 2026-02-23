using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Ipc;
using HyPrism.Services.Core.Ipc.Runtime;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.User;
using Microsoft.Extensions.DependencyInjection;

namespace HyPrism.Hosts;

public sealed class TauriRuntimeHost : IRuntimeHost
{
    public string Id => "tauri";
    public string Name => "Tauri bridge (experimental)";

    public async Task RunAsync(IServiceProvider services)
    {
        Logger.Info("Boot", "Starting Tauri bridge runtime...");

        // Register IPC handlers so channels can be served over Tauri bridge.
        var ipcService = services.GetRequiredService<IpcService>();
        ipcService.RegisterAll();

        // Keep startup behavior consistent with Electron host.
        var instanceService = services.GetRequiredService<IInstanceService>();
        instanceService.MigrateLegacyData();
        instanceService.MigrateVersionFoldersToIdFolders();

        var profileManagementService = services.GetRequiredService<IProfileManagementService>();
        profileManagementService.InitializeProfileModsSymlink();

        var bridge = TauriIpcRuntimeBridge.Instance;
        bridge.Start();

        Logger.Success("Boot", "Tauri bridge runtime ready");
        await bridge.Completion;
    }
}
