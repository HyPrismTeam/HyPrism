# Services Reference

All services are registered as singletons in `Bootstrapper.cs` and injected via constructor.

## Core Services (`Services/Core/`)

### IpcService
- **File:** `Services/Core/IpcService.cs`
- **Purpose:** Central IPC channel registry — single source of truth for all React ↔ .NET communication
- **Key method:** `RegisterAll()` — registers all IPC handlers
- **Annotations:** Contains `@type` and `@ipc` doc comments used by code generator
- **Domains:** config, game, news, profile, settings, i18n, window, browser, mods, console
- **Instance saves handlers:** supports listing saves, opening save folders, and deleting save folders via IPC (`hyprism:instance:saves`, `hyprism:instance:openSaveFolder`, `hyprism:instance:deleteSave`)
- **Folder picker timeout:** `hyprism:file:browseFolder` uses extended timeout (300s) to allow manual directory selection without frontend timeout.
- **Mods target resolution:** mod IPC handlers resolve the target from installed instance metadata (including latest) and avoid implicit `branch/latest` placeholder fallback.
- **Mods exact targeting:** mod IPC accepts optional `instanceId`; when provided, it has priority over branch/version to prevent collisions between multiple instances with the same version.
- **Mods changelog:** `hyprism:mods:changelog` returns the (best-effort) plaintext changelog for a specific CurseForge mod file (`modId` + `fileId`).
- **Instance operations targeting:** instance delete/saves IPC handlers accept `instanceId` and resolve by GUID first, with branch/version kept only as backward-compatible fallback.
- **Instance icon refresh:** `hyprism:instance:getIcon` returns a cache-busted file URL (`?v=<lastWriteTicks>`) so updated logos appear immediately after overwrite.
- **Frontend icon loading rule:** instance list icon requests are executed sequentially (not in parallel) to avoid mixed responses on shared IPC reply channels.

### ConfigService
- **File:** `Services/Core/ConfigService.cs`
- **Type:** Singleton
- **Purpose:** Application configuration (persisted to JSON)
- **Config paths:**
  - Windows: `%APPDATA%/HyPrism/config.json`
  - Linux: `~/.config/HyPrism/config.json`
  - macOS: `~/Library/Application Support/HyPrism/config.json`

### Logger
- **File:** `Services/Core/Logger.cs`
- **Type:** Static class
- **Purpose:** Structured logging (Serilog backend + colored console + in-memory buffer)
- **Methods:** `Info()`, `Success()`, `Warning()`, `Error()`, `Debug()`, `Progress()`
- **Log files:** `{appDir}/Logs/{timestamp}.log`

### LocalizationService
- **File:** `Services/Core/LocalizationService.cs`
- **Type:** Singleton (Instance pattern)
- **Purpose:** Runtime language switching with nested key support
- **Locale files:** `Assets/Locales/{code}.json`

### BrowserService
- **File:** `Services/Core/BrowserService.cs`
- **Purpose:** Opens URLs in the system default browser

### DiscordService
- **File:** `Services/Core/DiscordService.cs`
- **Purpose:** Discord Rich Presence integration

### GitHubService
- **File:** `Services/Core/GitHubService.cs`
- **Purpose:** Release checking and self-update functionality

## Game Services (`Services/Game/`)

### GameSessionService
- **Purpose:** Manages game lifecycle — download, install, patch, launch
- **States:** preparing → download → install → patching → launching → running → stopped
- **Auth launch behavior:** In authenticated mode, launch identity/name is derived from token claims when available to avoid server-side username mismatch shutdowns.
- **Custom auth mode:** Non-official profiles can launch in online authenticated mode with client binary patching + DualAuth runtime agent for custom session domains.
- **Server JAR policy:** The launcher no longer rewrites `Server/HytaleServer.jar` during custom-auth launches.
- **Stop control:** Game stop is available through IPC (`hyprism:game:stop`) and can be triggered from Dashboard and Instances actions.
- **Official 403 recovery:** If official CDN download returns HTTP 403 (expired signed `verify` token), the service force-refreshes version cache and retries official download once before mirror fallback.
- **Selected instance launch sync:** selecting an instance updates both `SelectedInstanceId` and legacy launch fields (`VersionType`/`SelectedVersion`), and Dashboard launch sends explicit branch/version to avoid stale-target launches.
- **Launch path priority:** if `SelectedInstanceId` is set, `GameSessionService` resolves launch/install path by instance ID first (no fallback to another installed instance with same branch/version).
- **Latest metadata storage:** `latest.json` is stored under branch root (`Instances/<branch>/latest.json`) instead of `Instances/<branch>/latest/latest.json`, preventing accidental creation of placeholder `latest` instance folders.
- **Linux NVIDIA EGL fix:** in dedicated GPU mode, launcher exports `__EGL_VENDOR_LIBRARY_FILENAMES` to detected NVIDIA GLVND vendor JSON (e.g. `/usr/share/glvnd/egl_vendor.d/10_nvidia.json`) to avoid fallback to `llvmpipe` on affected systems.

### ClientPatcher ⚠️
- **File:** `Services/Game/ClientPatcher.cs`
- **CRITICAL:** Binary manipulation for game integrity
- **Rule:** NEVER modify without explicit instruction

### ModService
- **Purpose:** Mod listing, searching, and management (CurseForge integration)
- **Instance mods source:** Reads from `UserData/Mods` and falls back to file-system discovery (`.jar`, `.zip`, `.disabled`) when manifest entries are missing
- **Download URL fallback:** if CurseForge returns no `downloadUrl` and `/download-url` is forbidden/empty, the service derives a deterministic CDN URL from `fileId + fileName`.

## User Services (`Services/User/`)

### ProfileService
- **Purpose:** Player profile CRUD operations
- **Features:** Multiple profiles, avatar management, profile switching
- **Mods storage policy:** profile switching does not redirect `UserData/Mods` to `Profiles/.../Mods`; mods remain instance-local.
- **Profile folder format:** profile folders are stored under `Profiles/{profileId}` (GUID).
- **Legacy migration:** launcher attempts to migrate legacy name-based profile folders in `Profiles/` to ID-based layout at startup (best-effort, non-destructive merge when both folders exist).
- **Official profile auth routing:** switching to an official profile automatically sets auth domain to `sessionserver.hytale.com`.
