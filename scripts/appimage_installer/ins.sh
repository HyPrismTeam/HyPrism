#!/usr/bin/env bash
set -e

# ------------------------------
# Check Python3
# ------------------------------
command -v python3 >/dev/null 2>&1 || { echo "Python3 not found"; exit 1; }
echo "Python3 found"

# ------------------------------
# Check requests
# ------------------------------
python3 -c "import requests" 2>/dev/null || {
    echo "python-requests not found. Trying to install system package..."

    if command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --needed python-requests
    elif command -v apt >/dev/null 2>&1; then
        sudo apt update
        sudo apt install -y python3-requests
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y python3-requests
    elif command -v zypper >/dev/null 2>&1; then
        sudo zypper install -y python3-requests
    else
        echo "Unsupported package manager. Please install requests manually."
        exit 1
    fi
}

# ------------------------------
# Check FUSE3
# ------------------------------
command -v fusermount3 >/dev/null 2>&1 || { echo "FUSE3 not found. Please install fuse3."; exit 1; }

# ------------------------------
# Start Python-file
# ------------------------------
curl -L -o appimageInstaller \
https://github.com/yyyumeniku/HyPrism/releases/latest/download/appimageInstaller
chmod +x appimageInstaller
./appimageInstaller --yes

echo "Done! HyPrism should now run."
