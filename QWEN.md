# HyPrism Project Context

## Project Overview

**HyPrism** is a cross-platform launcher for the game Hytale, built with .NET 10 and Sciter (EmptyFlow.SciterAPI). It provides mod management, multi-instance support, and an alternative to the official launcher.

### Architecture

The application follows a **Console + IPC + Preact SPA** pattern:

- **Backend**: .NET 10 console application with dependency injection
- **Frontend**: Preact 10 + TypeScript SPA rendered in a Sciter native window
- **Communication**: IPC via Sciter `xcall('hyprismCall', channel, json)` and `__hyprismReceive` global (named channels: `hyprism:{domain}:{action}`)
- **Logging**: Serilog

### Key Technologies

| Layer | Technology |
|-------|------------|
| Backend | .NET 10, EmptyFlow.SciterAPI, Serilog, DiscordRichPresence |
| Frontend | Preact 10, TypeScript, Vite, TailwindCSS, Framer Motion, i18next |
| IPC | Sciter xcall / __hyprismReceive (in-process, no socket) |
| DI | Microsoft.Extensions.DependencyInjection |

### Project Structure

```
HyPrism/
├── Program.cs              # Entry point, Sciter bootstrap
├── Bootstrapper.cs         # DI container setup
├── HyPrism.csproj          # .NET project file
├── Frontend/               # React SPA source
│   ├── src/
│   │   ├── lib/ipc.ts      # Generated IPC types (do not edit)
│   │   ├── components/
│   │   └── pages/
│   └── package.json
├── Services/               # Business logic layer
│   ├── Core/               # Infrastructure, IPC, Platform, Integration
│   ├── Game/               # Game-specific services (Launch, Mod, Version, etc.)
│   └── User/               # User profiles, authentication, skins
├── Docs/                   # User and technical documentation (EN/RU)
├── Scripts/                # Build and codegen scripts
└── wwwroot/                # Built frontend (generated)
```

## Building and Running

### Prerequisites

- **.NET 10.0 SDK**
- **Node.js 20+** (for frontend build)

### Build Commands

```bash
# Full build (includes frontend via npm ci + npm run build)
dotnet build

# Run the launcher
dotnet run

# Frontend development (separate terminal)
cd Frontend && npm run dev
```

### Build Process

The `.csproj` file defines automatic frontend build targets:

1. `NpmInstall` — Runs `npm ci` when `package.json` or `package-lock.json` changes
2. `GenerateIpcTs` — Runs `node Scripts/generate-ipc.mjs` to generate `Frontend/src/lib/ipc.ts` from `IpcService.cs`
3. `BuildFrontend` — Runs `npm run build` (Vite) to produce `wwwroot/`
4. `CopyFrontendDist` — Copies `wwwroot/` to `$(OutputPath)/wwwroot/`

### Release Build

```bash
dotnet publish -c Release
```

Release configuration enables:
- ReadyToRun AOT compilation
- Self-contained deployment
- Disabled asset compression

## Development Conventions

### Adding a Feature

1. Add/update .NET service in `Services/{Core|Game|User}/`
2. Register in `Bootstrapper.cs` if new service
3. Add IPC handler with `@ipc` annotation in `IpcService.cs`
4. Add `@type` annotation if new TypeScript type needed
5. Regenerate IPC types: `node Scripts/generate-ipc.mjs`
6. Create React component/page in `Frontend/src/`
7. Update documentation in `Docs/` (both English and Russian)

### IPC Channel Naming

Channels follow the pattern: `hyprism:{domain}:{action}`

Examples:
- `hyprism:game:launch`
- `hyprism:settings:get`
- `hyprism:i18n:set`

### Coding Standards

- **Async methods**: Suffix with `Async`
- **Interfaces**: Prefix with `I`, inject via constructor
- **Services**: Registered as singletons
- **No hardcoded values**: Use config, theme tokens, or localization keys
- **No manual edits** to `Frontend/src/lib/ipc.ts` (generated file)

### Critical Files

| File | Purpose | Caution |
|------|---------|---------|
| `Program.cs` | App entry point, Sciter bootstrap | Changes affect entire startup |
| `Bootstrapper.cs` | DI container setup | Breaking changes affect all services |
| `IpcService.cs` | IPC bridge between backend/frontend | Must stay in sync with frontend |
| `ClientPatcher.cs` | Game integrity | Never modify without explicit instruction |

### Documentation Policy

All changes must include documentation updates:
- **User docs** (`Docs/*/User/`) — UI or feature behavior changes
- **Developer docs** (`Docs/*/Development/`) — build/CI/workflow changes
- **API docs** (`Docs/*/Technical/`) — IPC channel changes

Both English (`Docs/English/`) and Russian (`Docs/Russian/`) versions should be updated.

## Testing

```bash
# Frontend type checking
cd Frontend && npx tsc --noEmit

# Full build verification
dotnet build  # Must complete with 0 errors
```

## Additional Notes

### Linux Packaging

- App ID: `io.github.HyPrismTeam.HyPrism`
- Icon source: `Frontend/public/icon.png` → generated to `Build/icons/` during packaging
- AppStream metadata: `Properties/linux/io.github.HyPrismTeam.HyPrism.metainfo.xml`
- Flatpak runtime: `24.08` with `org.electronjs.Electron2.BaseApp`

### Security Model

- `contextIsolation: true` — renderer has no access to Node.js
- `nodeIntegration: false` — no `require()` in renderer
- `preload.js` exposes only `window.electron.ipcRenderer` via `contextBridge`

### Memory Optimizations

The project uses several runtime optimizations in `runtimeconfig.template.json` and `.csproj`:
- Server GC disabled, concurrent GC enabled
- Large object heap compaction
- ReadyToRun compilation
- Disabled unnecessary features (EventSource, MetadataUpdater, etc.)

### Related Links

- GitHub: https://github.com/yyyumeniku/HyPrism
- GitLab: https://gitlab.com/yyyumeniku/HyPrism
- Website: https://yyyumeniku.github.io/hyprism-site/
- Discord: https://discord.com/invite/ekZqTtynjp
