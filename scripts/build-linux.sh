#!/bin/bash
set -e

# Linux Build Script for HyPrism
# Usage: ./scripts/build-linux.sh [version]
# Example: ./scripts/build-linux.sh 1.0.24

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if version is provided
if [ -z "$1" ]; then
    echo -e "${YELLOW}No version specified. Using git tag or 'dev'${NC}"
    # Try to get version from git tag
    if git describe --tags --exact-match 2>/dev/null; then
        VERSION=$(git describe --tags --exact-match | sed 's/^v//')
    else
        VERSION="dev-$(git rev-parse --short HEAD)"
    fi
else
    VERSION="$1"
fi

APP_TITLE="HyPrism - Hytale Launcher"

echo -e "${GREEN}╔══════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║       Building HyPrism for Linux (AMD64)            ║${NC}"
echo -e "${GREEN}║       Version: $VERSION${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"

# Check if wails is installed
if ! command -v wails &> /dev/null; then
    echo -e "${RED}Error: Wails CLI not found. Install it with:${NC}"
    echo "go install github.com/wailsapp/wails/v2/cmd/wails@latest"
    exit 1
fi

# Check dependencies
echo -e "${YELLOW}Checking dependencies...${NC}"
MISSING_DEPS=()

if ! pkg-config --exists gtk+-3.0; then
    MISSING_DEPS+=("libgtk-3-dev")
fi

if ! pkg-config --exists webkit2gtk-4.0; then
    MISSING_DEPS+=("libwebkit2gtk-4.0-dev")
fi

if ! pkg-config --exists webkit2gtk-4.1; then
    MISSING_DEPS+=("libwebkit2gtk-4.1-dev")
fi

if [ ${#MISSING_DEPS[@]} -ne 0 ]; then
    echo -e "${RED}Missing dependencies: ${MISSING_DEPS[*]}${NC}"
    echo -e "${YELLOW}Install with:${NC}"
    echo "sudo apt install ${MISSING_DEPS[*]}"
    exit 1
fi

# Build webkit2_41 version for AppImage (modern systems)
echo -e "${YELLOW}Building AppImage version (webkit2gtk-4.1)...${NC}"
wails build -clean -tags webkit2_41 -ldflags "-X 'HyPrism/app.AppVersion=$VERSION' -X 'HyPrism/app.AppTitle=$APP_TITLE'"

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

# Save webkit41 binary for AppImage
cp build/bin/HyPrism /tmp/HyPrism-webkit41
echo -e "${GREEN}✓ webkit41 build completed${NC}"

# Build webkit2gtk-4.0 version for Flatpak (older distros)
echo -e "${YELLOW}Building Flatpak version (webkit2gtk-4.0)...${NC}"
wails build -clean -ldflags "-X 'HyPrism/app.AppVersion=$VERSION' -X 'HyPrism/app.AppTitle=$APP_TITLE'"

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

# Save webkit40 binary for Flatpak
mv build/bin/HyPrism build/bin/HyPrism-webkit40
echo -e "${GREEN}✓ webkit40 build completed${NC}"

# Restore webkit41 as main binary for AppImage
mv /tmp/HyPrism-webkit41 build/bin/HyPrism

# Create releases directory
mkdir -p releases

# ========== Create AppImage ==========
echo -e "${YELLOW}Creating AppImage...${NC}"

# Download appimagetool if not present
if [ ! -f "appimagetool" ]; then
    echo "Downloading appimagetool..."
    wget -q https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage -O appimagetool
    chmod +x appimagetool
fi

# Create AppDir structure
rm -rf HyPrism.AppDir
mkdir -p HyPrism.AppDir/usr/bin
mkdir -p HyPrism.AppDir/usr/share/applications
mkdir -p HyPrism.AppDir/usr/share/icons/hicolor/512x512/apps
mkdir -p HyPrism.AppDir/usr/share/metainfo

# Copy binary
cp build/bin/HyPrism HyPrism.AppDir/usr/bin/
chmod +x HyPrism.AppDir/usr/bin/HyPrism

# Copy/create icon
if [ -f "build/appicon.png" ]; then
    cp build/appicon.png HyPrism.AppDir/usr/share/icons/hicolor/512x512/apps/hyprism.png
    cp build/appicon.png HyPrism.AppDir/hyprism.png
else
    echo -e "${YELLOW}Warning: build/appicon.png not found${NC}"
fi

# Create desktop file
cat > HyPrism.AppDir/hyprism.desktop << 'DESKTOP'
[Desktop Entry]
Name=HyPrism
Exec=HyPrism
Icon=hyprism
Type=Application
Categories=Game;
Terminal=false
StartupWMClass=HyPrism
Comment=A modern launcher for Hytale
DESKTOP

cp HyPrism.AppDir/hyprism.desktop HyPrism.AppDir/usr/share/applications/

# Create AppStream metadata
cat > HyPrism.AppDir/usr/share/metainfo/hyprism.appdata.xml << 'APPDATA'
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>dev.hyprism.HyPrism</id>
  <name>HyPrism</name>
  <summary>A modern launcher for Hytale</summary>
  <metadata_license>MIT</metadata_license>
  <project_license>MIT</project_license>
  <description>
    <p>HyPrism is an open-source, community-driven launcher for Hytale that provides mod management, multiple instances, and more.</p>
  </description>
  <categories>
    <category>Game</category>
  </categories>
</component>
APPDATA

# Create AppRun
cat > HyPrism.AppDir/AppRun << 'APPRUN'
#!/bin/bash
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export PATH="${HERE}/usr/bin:${PATH}"
export XDG_DATA_DIRS="${HERE}/usr/share:${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"

# Check for webkit2gtk-4.1
if ! ldconfig -p 2>/dev/null | grep -q "libwebkit2gtk-4.1.so.0"; then
    echo "Error: WebKit2GTK 4.1 required. Use Flatpak version for older systems."
    exit 1
fi

exec "${HERE}/usr/bin/HyPrism" "$@"
APPRUN
chmod +x HyPrism.AppDir/AppRun

# Build AppImage
APPIMAGE_NAME="HyPrism-v${VERSION}-x86_64.AppImage"
ARCH=x86_64 ./appimagetool --appimage-extract-and-run HyPrism.AppDir "$APPIMAGE_NAME"

if [ $? -eq 0 ]; then
    mv "$APPIMAGE_NAME" releases/
    echo -e "${GREEN}✓ AppImage created: releases/$APPIMAGE_NAME${NC}"
    echo -e "${GREEN}✓ Size: $(du -h "releases/$APPIMAGE_NAME" | cut -f1)${NC}"
fi

# ========== Create Flatpak ==========
echo -e "${YELLOW}Creating Flatpak...${NC}"

# Check if flatpak-builder is installed
if ! command -v flatpak-builder &> /dev/null; then
    echo -e "${RED}flatpak-builder not found. Install with:${NC}"
    echo "sudo apt install flatpak flatpak-builder"
    echo -e "${YELLOW}Skipping Flatpak build${NC}"
else
    # Add Flathub remote
    flatpak remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo

    # Install GNOME Platform 42
    if ! flatpak list | grep -q "org.gnome.Platform.*42"; then
        echo "Installing GNOME Platform 42..."
        sudo flatpak install -y flathub org.gnome.Platform//42 org.gnome.Sdk//42
    fi

    # Prepare Flatpak directory
    cd flatpak
    
    # Copy webkit40 binary
    cp ../build/bin/HyPrism-webkit40 HyPrism
    chmod +x HyPrism
    
    # Copy icon if needed
    if [ -f "../build/appicon.png" ]; then
        cp ../build/appicon.png dev.hyprism.HyPrism.png
    fi
    
    # Build Flatpak
    flatpak-builder --force-clean --repo=repo builddir dev.hyprism.HyPrism.json
    
    # Create bundle
    FLATPAK_NAME="HyPrism-v${VERSION}.flatpak"
    flatpak build-bundle repo "$FLATPAK_NAME" dev.hyprism.HyPrism
    
    if [ $? -eq 0 ]; then
        mv "$FLATPAK_NAME" ../releases/
        echo -e "${GREEN}✓ Flatpak created: releases/$FLATPAK_NAME${NC}"
        echo -e "${GREEN}✓ Size: $(du -h "../releases/$FLATPAK_NAME" | cut -f1)${NC}"
    fi
    
    # Cleanup
    rm -f HyPrism dev.hyprism.HyPrism.png
    rm -rf repo builddir .flatpak-builder
    
    cd ..
fi

# ========== Create tar.gz ==========
echo -e "${YELLOW}Creating tar.gz archive...${NC}"
TARBALL_NAME="HyPrism-v${VERSION}-linux-x86_64.tar.gz"
tar -czf "releases/$TARBALL_NAME" -C build/bin HyPrism
echo -e "${GREEN}✓ Tarball created: releases/$TARBALL_NAME${NC}"
echo -e "${GREEN}✓ Size: $(du -h "releases/$TARBALL_NAME" | cut -f1)${NC}"

# Cleanup
rm -rf HyPrism.AppDir

echo -e "${GREEN}╔══════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║       Build Complete!                                ║${NC}"
echo -e "${GREEN}║       Output: releases/                              ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"

echo -e "${YELLOW}To upload to GitHub release, run:${NC}"
echo "gh release upload v${VERSION} releases/HyPrism-v${VERSION}-*"
