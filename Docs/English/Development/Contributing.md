# Contributing

## Getting Started

1. Fork the repository
2. Clone your fork
3. Install prerequisites: .NET 10 SDK, Node.js 20+
4. Run `dotnet build` to build everything (including frontend)
5. Run `dotnet run` to launch

## Development Workflow

1. Create a feature branch from `main`
2. Make changes following [Coding Standards](CodingStandards.md)
3. Test: `dotnet build` must complete with 0 errors
4. For frontend changes, also verify `cd Frontend && npx tsc --noEmit`
5. Commit with clear messages
6. Open a Pull Request

### Linux packaging icon note

- For `.deb`/`.rpm` desktop icons, `Scripts/publish.sh` now prepares a Linux icon set under `Build/icons/` as size files (`16x16.png` … `512x512.png`) plus `icon.png`.
- Ensure `Build/icon.png` exists before running publish targets (`deb`, `rpm`, `linux`, `all`).
- Linux package app ID is `com.hyprismteam.hyprism`.
- AppStream metadata is injected for Linux packaging from `Packaging/linux/com.hyprismteam.hyprism.metainfo.xml`.

## Adding a New Feature

### Checklist

1. Add/update .NET service in `Services/{Core|Game|User}/`
2. Register in `Bootstrapper.cs` if new service
3. Add IPC handler + `@ipc` annotation in `IpcService.cs`
4. Add `@type` annotation if new TypeScript type is needed
5. Regenerate: `node Scripts/generate-ipc.mjs` (or `dotnet build`)
6. Create React component/page in `Frontend/src/`
7. Add route in `App.tsx` if new page
8. Update documentation in `Docs/`
9. Verify: `dotnet build` passes with 0 errors

### Adding an IPC Channel

See [IPC Code Generation](../Technical/IpcCodegen.md) for the full guide.

## Critical Files

| File | Impact | Rule |
|------|--------|------|
| `ClientPatcher.cs` | Game integrity | Never modify without explicit instruction |
| `Program.cs` | App entry point | Changes affect entire startup |
| `Bootstrapper.cs` | DI setup | Breaking changes affect all services |
| `IpcService.cs` | IPC bridge | Must stay in sync with frontend |
| `preload.js` | Security boundary | Minimal changes only |

## Documentation

Every change must include documentation updates:
- **User docs** — when UI or feature behavior changes
- **Developer docs** — when build/CI/workflows change
- **API docs** — when IPC channels are added, renamed, or removed

Both English and Russian docs should be updated.

## Code Review Guidelines

- Follows coding standards (naming, braces, async suffix)
- No hardcoded values — use config, theme tokens, localization keys
- IPC changes update both C# annotations and verify generated output
- No references to deprecated `UI/` directory
- No manual edits to `Frontend/src/lib/ipc.ts`
