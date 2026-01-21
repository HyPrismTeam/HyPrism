#!/bin/bash
set -e

# macOS Build Script for HyPrism
# Usage: ./scripts/build-macos.sh [version]
# Example: ./scripts/build-macos.sh 1.0.24

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
echo -e "${GREEN}║       Building HyPrism for macOS (ARM64)            ║${NC}"
echo -e "${GREEN}║       Version: $VERSION${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"

# Check if wails is installed
if ! command -v wails &> /dev/null; then
    echo -e "${RED}Error: Wails CLI not found. Install it with:${NC}"
    echo "go install github.com/wailsapp/wails/v2/cmd/wails@latest"
    exit 1
fi

# Build the application
echo -e "${YELLOW}Building application...${NC}"
wails build -clean -ldflags "-X 'HyPrism/app.AppVersion=$VERSION' -X 'HyPrism/app.AppTitle=$APP_TITLE'"

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Build completed successfully${NC}"

# Create DMG
echo -e "${YELLOW}Creating DMG installer...${NC}"

# Install create-dmg if not present
if ! command -v create-dmg &> /dev/null; then
    echo -e "${YELLOW}Installing create-dmg...${NC}"
    brew install create-dmg
fi

# Create a clean directory for DMG contents
rm -rf dmg-contents
mkdir -p dmg-contents

# Copy the app bundle
cp -r build/bin/HyPrism.app dmg-contents/

# Create the Fix-HyPrism helper script
cat > dmg-contents/Fix-HyPrism.command << 'HELPER'
#!/bin/bash
clear
echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║       HyPrism - Fix macOS Security Warning           ║"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# Check common locations for HyPrism.app
APP_PATH=""
if [ -d "/Applications/HyPrism.app" ]; then
    APP_PATH="/Applications/HyPrism.app"
elif [ -d "$HOME/Applications/HyPrism.app" ]; then
    APP_PATH="$HOME/Applications/HyPrism.app"
elif [ -d "$(dirname "$0")/HyPrism.app" ]; then
    APP_PATH="$(dirname "$0")/HyPrism.app"
fi

if [ -z "$APP_PATH" ]; then
    echo "❌ HyPrism.app not found!"
    echo ""
    echo "Please drag HyPrism.app to your Applications folder first,"
    echo "then run this script again."
    echo ""
    echo "Or run manually in Terminal:"
    echo "  xattr -cr /path/to/HyPrism.app"
    echo ""
    read -p "Press Enter to close..."
    exit 1
fi

echo "Found HyPrism at: $APP_PATH"
echo ""
echo "Removing quarantine attribute..."
xattr -cr "$APP_PATH"

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Success! HyPrism is now ready to use."
    echo ""
    echo "You can now open HyPrism from your Applications folder."
else
    echo ""
    echo "❌ Failed to remove quarantine. Try running manually:"
    echo "  sudo xattr -cr \"$APP_PATH\""
fi

echo ""
read -p "Press Enter to close..."
HELPER
chmod +x dmg-contents/Fix-HyPrism.command

# Create DMG
DMG_NAME="HyPrism-v${VERSION}-macOS-arm64.dmg"
rm -f "$DMG_NAME"

create-dmg \
  --volname "HyPrism" \
  --window-pos 200 120 \
  --window-size 700 400 \
  --icon-size 100 \
  --icon "HyPrism.app" 175 120 \
  --hide-extension "HyPrism.app" \
  --app-drop-link 525 120 \
  --icon "Fix-HyPrism.command" 350 280 \
  "$DMG_NAME" \
  "dmg-contents"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ DMG created: $DMG_NAME${NC}"
    echo -e "${GREEN}✓ Size: $(du -h "$DMG_NAME" | cut -f1)${NC}"
    
    # Move to releases directory
    mkdir -p releases
    mv "$DMG_NAME" releases/
    
    echo -e "${GREEN}╔══════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║       Build Complete!                                ║${NC}"
    echo -e "${GREEN}║       DMG: releases/$DMG_NAME${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"
else
    echo -e "${RED}Failed to create DMG${NC}"
    exit 1
fi

# Cleanup
rm -rf dmg-contents

echo -e "${YELLOW}To upload to GitHub release, run:${NC}"
echo "gh release upload v${VERSION} releases/${DMG_NAME}"
