#!/usr/bin/env python3
import datetime
import shutil
import requests
import os
import sys
from pathlib import Path

OWNER = "Aarav2709"
REPO = "HyPrism"
HEADERS = {"User-Agent": "HyPrism-installer"}


def backup_zip(install_dir: Path):
    print("Backup Hytale")
    y = input("Do you want backup(just in case)[y/N]?: ").lower()

    if y not in ("y","yes"):
        
        pass
    else: 
        print("Skipping backup")
        return

    hyprism_path = input("Path to your current HyPrism/game_version (not HyPrism folder): ").strip()
    hyprism_path = Path(hyprism_path).expanduser().resolve()

    if not hyprism_path.exists():
        print(f"Path {hyprism_path} does not exist. Skipping backup.")
        return

    # Check write access
    if not os.access(hyprism_path, os.W_OK):
        if os.geteuid() != 0 and "--elevated" not in sys.argv:
            os.execvp(
                "pkexec",
                ["pkexec", sys.executable] + sys.argv + ["--elevated"]
            )
        elif os.geteuid() != 0:
            raise PermissionError("pkexec failed or was cancelled")


    timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_path = install_dir / f"HyPrism_backup_{timestamp}"

    print(f"Creating backup: {backup_path}.zip")
    shutil.make_archive(str(backup_path), 'zip', hyprism_path)
    print("Backup created successfully.")


def install_hyprism(install_dir: Path) -> Path:
    install_dir.mkdir(parents=True, exist_ok=True)

    api_url = f"https://api.github.com/repos/{OWNER}/{REPO}/releases/latest"
    resp = requests.get(api_url, headers=HEADERS)
    resp.raise_for_status()
    release = resp.json()

    asset_url = None
    for asset in release.get("assets", []):
        name = asset.get("name", "")
        if name.endswith(".AppImage") and "x86_64" in name:
            asset_url = asset.get("browser_download_url")
            break

    if not asset_url:
        raise RuntimeError("AppImage not found in latest release")

    filename = install_dir / "HyPrism.AppImage"
    temp_file = filename.with_suffix(".tmp")

    print(f"Downloading latest HyPrism...")
    with requests.get(asset_url, stream=True, headers=HEADERS) as r:
        r.raise_for_status()
        with open(temp_file, "wb") as f:
            for chunk in r.iter_content(8192):
                f.write(chunk)

    if filename.exists():
        filename.unlink()
    temp_file.rename(filename)
    filename.chmod(0o755)

    print(f"HyPrism installed/updated successfully at: {filename}")
    return filename


def ins_shortcut(app_path: Path, install_dir: Path):
    desktop_entry_template = f"""[Desktop Entry]
Name=HyPrism
Comment=Hytale Launcher
Exec={app_path}
TryExec={app_path}
Icon={install_dir}/HyPrism_icon.png
Terminal=false
Type=Application
Categories=Game;
StartupWMClass=HyPrism
"""

    icon_src = Path(__file__).parent / "HyPrism_icon.png"
    icon_dst = install_dir / "HyPrism_icon.png"
    if icon_src.exists():
        shutil.copy2(icon_src, icon_dst)

    paths = [
        Path.home() / "Desktop" / "HyPrism.desktop",
        Path.home() / ".local" / "share" / "applications" / "HyPrism.desktop"
    ]

    for p in paths:
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(desktop_entry_template)
        p.chmod(0o755)

    print("Shortcuts created/updated.")


def main():
    if len(sys.argv) > 1:
        install_dir = Path(sys.argv[1]).expanduser().resolve()
    else:
        install_dir = Path.home() / "Applications" / "HyPrism"

    app_file = install_dir / "HyPrism.AppImage"
    is_update = app_file.exists()

    if is_update:
        print(f"Update detected in {install_dir}")
        backup_zip(install_dir)

    try:
        app_path = install_hyprism(install_dir)

        if not is_update:
            ans = input("Do you want to create a desktop shortcut? [y/N]: ").lower()
            if ans in ('y', 'yes'):
                ins_shortcut(app_path, install_dir)
            else:
                print("Ok, skipping shortcuts.")
        else:
            # Always update shortcuts on update
            ins_shortcut(app_path, install_dir)

        print("Done!")

    except Exception as e:
        print(f"Error occurred: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()