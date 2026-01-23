# HyPrism Linux Installer

This document explains how to install, update, and run **HyPrism** on Linux using the provided bash and Python installer.

## Features

* Downloads the **latest `x86_64` AppImage** from the official HyPrism GitHub releases.
* Supports **updates**: detects existing installation and replaces the AppImage safely.
* **Optional backup** of an existing HyPrism / Hytale game version before updating (ZIP archive).
* Automatically handles **permission issues** using `pkexec` if required.
* Makes the AppImage executable.
* Creates or updates **Desktop and application menu shortcuts** (`.desktop` files).
* Copies and uses the provided `HyPrism_icon.png`.
* Uses GitHub API with a custom User-Agent.

## Requirements

* Linux system
* Bash
* Python 3
* `pip`
* `pkexec` (usually provided by `polkit`, only needed if backup path requires elevated permissions)
* Internet connection

## Usage

### 1. Make the bash script executable

```bash
chmod +x install.sh
```

### 2. Run the installer

```bash
./install.sh [installation_directory]
```

* `[installation_directory]` is optional
* Default: `~/Applications/HyPrism`

### 3. What the installer does

#### First install

* Downloads the latest HyPrism AppImage
* Makes it executable
* Asks whether to create desktop shortcuts
* Copies the icon
* Creates:

  * `~/Desktop/HyPrism.desktop`
  * `~/.local/share/applications/HyPrism.desktop`

#### Update (if HyPrism already exists)

* Detects an existing installation automatically
* **Asks if you want to create a backup** (recommended)
* If enabled:

  * Prompts for the path to your existing `HyPrism/game_version` directory
  * Creates a timestamped ZIP backup:

    ```
    HyPrism_backup_YYYYMMDD_HHMMSS.zip
    ```
* Downloads and replaces the AppImage
* **Always updates shortcuts automatically**

## Backup behavior

* Backup is **optional**
* Only performed during updates
* If the selected directory requires elevated permissions, the installer will re-run itself using `pkexec`
* If backup is skipped or fails, installation continues normally

## Notes

* Tested on **Arch Linux**
* Should work on most modern Linux distributions with Python 3 and bash
* If the icon does not appear in your menu:

  * Ensure `HyPrism_icon.png` exists in the installation directory
  * You may need to log out and log back in
* You can safely re-run the installer at any time to:

  * Update HyPrism
  * Recreate shortcuts
  * Create new backups

## Troubleshooting

* **`AppImage not found in latest release`**

  * The GitHub release may not contain an `x86_64` AppImage
* **Permission denied during backup**

  * Accept the `pkexec` prompt or choose a directory you own
* **Shortcuts not showing**

  * Check file permissions:

    ```bash
    chmod +x ~/.local/share/applications/HyPrism.desktop
    ```

