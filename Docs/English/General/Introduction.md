# Introduction

**HyPrism** is a cross-platform Hytale game launcher built with modern technologies.

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# 13 |
| Desktop Shell | Sciter (EmptyFlow.SciterAPI 1.2.3) |
| Frontend | Preact 10 + TypeScript + Vite |
| Animations | Framer Motion (via preact/compat) |
| Styling | TailwindCSS v3 |
| Icons | Lucide React (via preact/compat) |
| Routing | React Router DOM (via preact/compat) |
| DI | Microsoft.Extensions.DependencyInjection |
| Logging | Serilog |
| Localization | i18next (12 languages) |

## How It Works

HyPrism runs as a **.NET Console Application** that creates a **Sciter native window**. Sciter loads the Preact SPA directly from the local filesystem. All communication between the Preact frontend and .NET backend happens through **IPC channels** (Inter-Process Communication).

```
.NET Console App → creates Sciter native window
  ├── SciterAPIHost
  │     └── Frameless native window
  │           └── SciterIpcBridge (WindowEventHandler)
  └── Preact SPA (loaded from wwwroot/index.html)
        └── ipc.ts → xcall(’hyprismCall’) → IpcService.cs → .NET Services
```

This is **NOT** a web server — there is no ASP.NET, no HTTP, no REST. The frontend calls the backend via `xcall('hyprismCall', channel, json)` and receives push events through the `__hyprismReceive` global, all in-process without any socket.

## Key Principles

1. **Single source of truth** — C# annotations in `IpcService.cs` define all IPC channels and TypeScript types; the frontend IPC client is 100% auto-generated
2. **In-process isolation** — Frontend runs inside Sciter's scripting engine; all native API access is channelled exclusively through `SciterIpcBridge`, no Node.js globals or require()
3. **DI everywhere** — All .NET services registered in `Bootstrapper.cs` via constructor injection
4. **Cross-platform** — Windows, Linux, macOS support via .NET 10 + Sciter
5. **Instance-based** — Each game installation is isolated in its own GUID-based folder

## Supported Platforms

- **Windows** 10/11 (x64)
- **Linux** (x64) — AppImage, Flatpak
- **macOS** (x64, arm64)

## Supported Languages

HyPrism supports 12 languages with runtime switching:

| Code | Language |
|------|----------|
| en-US | English |
| ru-RU | Russian |
| de-DE | German |
| es-ES | Spanish |
| fr-FR | French |
| ja-JP | Japanese |
| ko-KR | Korean |
| pt-BR | Portuguese (Brazil) |
| tr-TR | Turkish |
| uk-UA | Ukrainian |
| zh-CN | Chinese (Simplified) |
| be-BY | Belarusian |
