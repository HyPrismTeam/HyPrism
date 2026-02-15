# Configuration

HyPrism stores its configuration in `config.json` inside the data directory.

## Settings

Access settings through the **Settings** page (gear icon in sidebar).

### General

| Setting | Description | Default |
|---------|-------------|---------|
| Language | UI language (12 available) | System language or en-US |
| Close after launch | Close launcher when game starts | false |
| Launch on startup | Auto-start with OS | false |
| Minimize to tray | Minimize to system tray | false |

### Appearance

| Setting | Description | Default |
|---------|-------------|---------|
| Accent color | Theme accent color | Purple (#7C5CFC) |
| Animations | Enable UI animations | true |
| Transparency | Glass-morphism effects | true |
| Background mode | Dashboard background style | default |

### Game

| Setting | Description | Default |
|---------|-------------|---------|
| Resolution | Game window resolution | 1920x1080 |
| RAM allocation | Memory for game (MB) | 4096 |
| Sound | Game sound enabled | true |
| GPU preference | Graphics adapter selection | auto |

- **Optimization mods installer** now asks which instance should receive optimization mods before installation.

#### GPU Preference Options

| Value | Description |
|-------|-------------|
| auto | Let the system choose the best GPU |
| dedicated | Force dedicated graphics (NVIDIA/AMD) |
| integrated | Force integrated graphics (Intel/AMD) |

### Advanced

| Setting | Description | Default |
|---------|-------------|---------|
| Developer mode | Show developer tools | false |
| Verbose logging | Extended log output | false |
| Pre-release | Receive pre-release updates | false |
| Launcher branch | Release or pre-release channel | release |
| Data directory | Custom data storage path | Platform default |

## Instance Management

Instead of a single game installation, HyPrism uses **instances** — isolated game installations in separate folders.

### Instance Structure

Each instance is stored in a version-based folder under its branch:

```
Instances/
└── release/
	├── v8/
	│   ├── game/           # Game files
	│   ├── mods/           # Installed mods
	│   └── meta.json       # Instance metadata (includes internal ID)
	├── latest/
	│   └── ...
	└── ...
```

### Managing Instances

- **Create** — Download a new game installation
- **Switch** — Select which instance to launch
- **Delete** — Remove an instance (confirmation required)
- **View details** — See version, patch status, installed mods
- **Dashboard instance shortcut** — Click the icon placeholder left of Play to open the Instances page focused on the current selected instance
- **Switcher layout behavior** — Instance switcher and main action button are centered together as a single control group
- **Dashboard icon fallback** — If a custom icon cannot be loaded, the switcher now falls back to the version badge instead of showing an empty icon slot
- **Centered play action** — The main Play button stays centered on the dashboard even when the instance switcher is visible
- **Per-instance icon fidelity** — Dashboard icon mapping is keyed per unique instance identity to prevent one custom icon from being shown on other entries
- **Full icon tiles** — Custom instance icons fill their switcher tiles for clearer visual identity
- **Startup icon detection** — Dashboard retries selected-instance icon loading during startup so custom icons appear without manually switching instances
- **Tighter dashboard spacing** — The Play row is positioned closer to the disclaimer badge

### Data Folder Quick Action

- In **Settings → Data**, the **Open Launcher Folder** button opens the launcher data directory in your file manager.

## Profiles

HyPrism supports multiple player profiles. Switch between profiles via the sidebar profile selector.

### Profile Data

Each profile stores:
- **Nickname** — Display name in-game
- **UUID** — Unique player identifier
- **Avatar** — Profile picture (optional)
- **Skin backup** — Saved skin data

### Skin Backup

Profiles can back up your Hytale skin. Backups are stored in:

```
Profiles/
├── {ProfileUUID}/
│   ├── profile.json    # Profile metadata
│   └── skin.png        # Backed up skin
└── ...
```

Use the profile menu to:
- **Backup skin** — Save current skin to profile
- **Restore skin** — Apply backed up skin to account

## Mod Compatibility Safety

Before launch, HyPrism validates `UserData/Mods` for known-incompatible server mod metadata.

- Mods with a `ServerVersion` in the format `YYYY.MM.DD-<build>` are automatically moved to:
- This prevents Hytale's singleplayer server crash (`Invalid X-Range` / `Server failed to boot`).
- You can re-enable a moved mod manually by moving the `.jar` back to `UserData/Mods`.

## Installed Mods Selection Shortcuts

In both **Installed Mods** and **Browse Mods** tabs, HyPrism supports faster multi-select for mods:

- **Click** selects a single mod.
- **Ctrl/Cmd + Click** toggles a mod in the selection.
- **Shift + Click** replaces the current selection with the range from your anchor mod to the clicked mod.

When one or more mods are selected in **Installed Mods**, bulk actions (like **Enable Selected** / **Disable Selected**) apply to the entire selection.

## Drag-and-Drop Mod Import

- In **Installed Mods**, you can import mods by dragging files into the mods list.
- Supported drop formats: `.jar`, `.zip`, `.disabled`.
- Very large files and unsupported formats are skipped to prevent freezes.
- After importing, the mods list refreshes automatically.
- Selection does not persist when switching tabs or instances.

## Instances and Worlds Quick Actions

- In the instance list, **Right Click** opens the same instance actions menu as the 3-dots button (Edit, Open Folder, Open Mods Folder, Export, Delete).
- In the **Worlds** tab, world cards now expose hover actions for **Open Folder** and **Delete**.
- Instance content tabs now use localized labels for **Installed Mods** and **Browse Mods** across all supported UI languages.

## CurseForge Mod Page Shortcut

- Clicking a mod name in the mod lists/details opens that mod's CurseForge page in your default browser.

## Logs in Settings

- The launcher logs are available directly inside the **Settings** sidebar as a dedicated **Logs** tab.
- The Logs tab fills the settings content area and keeps its scroll region aligned to the panel border.
- Logs are no longer shown as a separate main navigation page.
- In embedded Settings mode, the Logs header matches other settings sections (text header, no icon).
- The logs output panel uses a slightly lighter background for improved readability.

## macOS Menu Bar

- On macOS, HyPrism provides launcher actions in the app menu bar (for example **Settings**, **Instances**, and **Quit**).

## Default Mods Folder

- The default managed mods directory is under instance user data:
	- `HyPrism/Instances/<branch>/<instance-guid>/UserData/Mods`
- This replaces legacy `Client/mods` for default mod storage and operations.
- Profile switching does not re-route this folder to `Profiles/.../Mods`; it stays inside the selected instance.

## Custom Auth Launch Behavior

For non-official profiles using custom auth domains, HyPrism launches in **online authenticated mode**.

- Only the client binary is patched for custom auth domains.
- Server auth handling uses the DualAuth runtime agent (no `Server/HytaleServer.jar` rewrite).
- The auth domain is used as entered (for example `auth.example.com`); HyPrism no longer forces `sessions.` prefix.
- For compatibility, if direct host fails, HyPrism also tries `sessions.<your-domain>` automatically.
- Launch identity prefers auth-server profile name fields to reduce owner-name/token mismatch issues.
- Dashboard and Instances views both expose game stop controls while the game is running.

## Configuration File

**Location:**
- Windows: `%APPDATA%/HyPrism/config.json`
- Linux: `~/.local/share/HyPrism/config.json`
- macOS: `~/Library/Application Support/HyPrism/config.json`

The config file is JSON and can be edited manually, but it's recommended to use the Settings page.

### Data Directory

HyPrism uses a fixed launcher data directory based on your platform default.

- The path is shown in **Settings** → **Data**
- Launcher data directory relocation is not supported
- The launcher provides an **Open** button to open the containing folder
