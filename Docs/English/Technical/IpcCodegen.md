# IPC Code Generation

HyPrism's IPC bridge between Preact and .NET is **100% auto-generated** from C# annotations. There are zero hand-written TypeScript IPC types or helpers.

## How It Works

1. Developer adds `@ipc` and `@type` annotations in C# doc comments in `Services/Core/IpcService.cs`
2. `Scripts/generate-ipc.mjs` parses these annotations
3. Generator outputs a single self-contained `Frontend/src/lib/ipc.ts`
4. MSBuild runs this automatically before every frontend build

## Annotation Reference

### `@ipc` — Define IPC Channels

Placed on individual handler registrations as doc comments:

```csharp
/// @ipc invoke hyprism:settings:get -> SettingsSnapshot
_bridge.On("hyprism:settings:get", async (args) => { ... });
```

**Syntax:** `@ipc {type} {channel} [-> {ReturnType}]`

| Type | Description | Generated Code |
|------|-------------|----------------|
| `invoke` | Request/reply | `invoke<ReturnType>(channel, data)` |
| `send` | Fire-and-forget | `send(channel, data)` |
| `event` | Push from .NET → React | `on(channel, callback)` |

### `@type` — Define TypeScript Interfaces

Placed in the class-level doc comment block:

```csharp
/// @type SettingsSnapshot {
///   language: string;
///   musicEnabled: boolean;
///   accentColor: string;
///   ramMb?: number;
///   [key: string]: unknown;
/// }
```

**Rules:**
- One `@type` per interface, can span multiple lines
- Use TypeScript syntax for fields (`name: type`, `name?: type`)
- Supports union types (`'starting' | 'running' | 'stopped'`)
- Supports index signatures (`[key: string]: unknown`)
- Supports arrays (`string[]`, `ModItem[]`)

## Generated Output

`Frontend/src/lib/ipc.ts` contains (in order):

1. **Sciter transport bootstrap** — `Window.this.xcall` sender + `__hyprismReceive` listener registry
2. **Core helpers** — `send()`, `on()`, `invoke<T>()`
3. **TypeScript interfaces** — all `@type` definitions
4. **Domain API objects** — typed methods per domain
5. **Unified `ipc` export** — single entry point for consumers

```typescript
// Consumer usage
import { ipc } from '../lib/ipc';
import type { SettingsSnapshot } from '../lib/ipc';

const settings = await ipc.settings.get();
ipc.windowCtl.minimize();
ipc.game.onProgress((data) => console.log(data.progress));
```

### Domain Name Conflicts

Domains named `window` or `console` are renamed in the export to avoid shadowing JavaScript globals:

| IPC Domain | Export Name |
|-----------|-------------|
| `window` | `ipc.windowCtl` |
| `console` | `ipc.consoleCtl` |

## MSBuild Integration

The `GenerateIpcTs` target in `HyPrism.csproj` runs before `BuildFrontend`:

```xml
<Target Name="GenerateIpcTs"
        BeforeTargets="BuildFrontend"
        Inputs="Services/Core/IpcService.cs"
        Outputs="Frontend/src/lib/ipc.ts">
  <Exec Command="node Scripts/generate-ipc.mjs" />
</Target>
```

MSBuild uses incremental build: if `IpcService.cs` hasn't changed, codegen is skipped.

## Adding a New IPC Channel

1. **Add handler** in `IpcService.cs` with `@ipc` annotation:
   ```csharp
   /// @ipc invoke hyprism:myDomain:myAction -> MyResult
   _bridge.On("hyprism:myDomain:myAction", async (args) => { ... });
   ```

2. **Add type** (if new) in the class doc comment:
   ```csharp
   /// @type MyResult { success: boolean; data: string; }
   ```

3. **Regenerate**: `node Scripts/generate-ipc.mjs` (or just `dotnet build`)

4. **Use in Preact**:
   ```typescript
   const result = await ipc.myDomain.myAction();
   ```
