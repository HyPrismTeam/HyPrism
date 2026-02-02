# HyPrism Linux Installer

This document explains how to **install, update, and run HyPrism** on Linux using the provided Python installer.

## Features

* Downloads the **latest `x64` AppImage** from the official HyPrism GitHub releases.
* Supports **updates**: detects existing installation and replaces the AppImage safely.
* **Optional backup** of an existing HyPrism / Hytale game version before updating (ZIP archive).
* Makes the AppImage executable.
* Creates or updates **Desktop and application menu shortcuts** (`.desktop` files).
* Uses GitHub API with a custom User-Agent.
* Minimal **environment checker** included as a bash script to verify Python, pip, and FUSE support.

---

## Requirements

* Linux system
* Python 3
* `pip`
* Bash (for environment check)
* `fusermount3` (FUSE3 support)
* Internet connection

---

## Usage

### 1. Optional: check environment

The provided bash script will check your system for Python 3, pip, and FUSE3, and create a virtual environment with required Python packages:

```bash
chmod +x install.sh
./install.sh
```

**What it does:**

* Verifies Python 3 and pip are installed
* Verifies FUSE3 (`fusermount3`) is available
* Creates a Python virtual environment `.venv`
* Installs `requests` in the virtual environment

**Output example:**

```
Python 3 found
pip found
FUSE3 found
Environment check passed.
You can now run: python3 main.py <arguments>
```

---

### 2. Run the Python installer

After checking the environment, you can run the installer with:

```bash
python3 main.py [OPTIONS]
```

**Default installation path:** `~/Applications/HyPrism`

---

### 3. Available options

| Option          | Description                              |
| --------------- | ---------------------------------------- |
| `--dir=PATH`    | Specify a custom installation directory  |
| `--no-shortcut` | Skip creating desktop and menu shortcuts |
| `--no-backup`   | Skip creating a backup during updates    |
| `-c` / `-coffe` | Enable “coffee mode” (fun easter egg)    |

---

### 4. What the installer does

#### First install

* Downloads the latest HyPrism AppImage
* Makes it executable
* Prompts whether to create desktop shortcuts
* Copies `HyPrism_icon.png`
* Creates:

  * `~/Desktop/HyPrism.desktop`
  * `~/.local/share/applications/HyPrism.desktop`

#### Update (if HyPrism already exists)

* Detects existing installation automatically

* Prompts **whether to create a backup** (recommended)

* If backup is enabled:

  * Prompts for the path to your existing `HyPrism/game_version` directory
  * Creates a timestamped ZIP backup:

    ```
    HyPrism_backup_YYYYMMDD_HHMMSS.zip
    ```

* Downloads and replaces the AppImage

* **Always updates shortcuts automatically**

---

## Backup behavior

* Backup is **optional** and only performed during updates
* If the selected directory requires elevated permissions, the installer will re-run itself using `pkexec`
* If backup is skipped or fails, installation continues normally

---

## Notes

* Tested on **Arch Linux**, should work on most modern Linux distributions with Python 3 and bash

* You can safely re-run the installer at any time to:

  * Update HyPrism
  * Recreate shortcuts
  * Create new backups

* If the icon does not appear in your menu:

  * Ensure `HyPrism_icon.png` exists in the installation directory
  * You may need to log out and log back in

---

## Troubleshooting

* **Python 3 or pip not found**

  * Make sure Python 3 and pip are installed and in your PATH

* **FUSE3 not found (`fusermount3`)**

  * Install FUSE3 via your distribution’s package manager

* **AppImage not found in latest release**

  * The GitHub release may not contain an `x64` AppImage

* **Permission denied during backup**

  * Accept the `pkexec` prompt or choose a directory you own

* **Shortcuts not showing**

  * Check file permissions:

    ```bash
    chmod +x ~/.local/share/applications/HyPrism.desktop
    ```

  * Or reboot your desktop environment
