#!/bin/bash
set -e

# Create GitHub Release Script
# Usage: ./scripts/create-release.sh <version>
# Example: ./scripts/create-release.sh 1.0.24

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

if [ -z "$1" ]; then
    echo -e "${RED}Error: Version number required${NC}"
    echo "Usage: ./scripts/create-release.sh <version>"
    echo "Example: ./scripts/create-release.sh 1.0.24"
    exit 1
fi

VERSION="$1"
TAG="v${VERSION}"

echo -e "${GREEN}╔══════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║       Creating GitHub Release: ${TAG}${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"

# Check if gh CLI is installed
if ! command -v gh &> /dev/null; then
    echo -e "${RED}Error: GitHub CLI (gh) not found${NC}"
    echo "Install from: https://cli.github.com/"
    exit 1
fi

# Check if authenticated
if ! gh auth status &> /dev/null; then
    echo -e "${YELLOW}Not authenticated with GitHub. Running gh auth login...${NC}"
    gh auth login
fi

# Check if releases directory exists
if [ ! -d "releases" ]; then
    echo -e "${RED}Error: releases/ directory not found${NC}"
    echo "Build the binaries first:"
    echo "  macOS: ./scripts/build-macos.sh ${VERSION}"
    echo "  Linux: ./scripts/build-linux.sh ${VERSION}"
    exit 1
fi

# List available files
echo -e "${YELLOW}Available files in releases/:${NC}"
ls -lh releases/

# Create git tag
echo -e "${YELLOW}Creating git tag ${TAG}...${NC}"
if git tag -l | grep -q "^${TAG}$"; then
    echo -e "${YELLOW}Tag ${TAG} already exists${NC}"
else
    git tag -a "${TAG}" -m "Release ${VERSION}"
    git push origin "${TAG}"
    echo -e "${GREEN}✓ Tag created and pushed${NC}"
fi

# Create release notes
RELEASE_NOTES=$(cat << EOF
## HyPrism ${VERSION}

### Installation

**macOS (Apple Silicon)**
- Download \`HyPrism-v${VERSION}-macOS-arm64.dmg\`
- Open the DMG and drag HyPrism to Applications
- If you get a security warning, run \`Fix-HyPrism.command\` from the DMG

**Linux**
- **Flatpak** (recommended for older distros like Ubuntu 22.04):
  \`\`\`bash
  flatpak install HyPrism-v${VERSION}.flatpak
  flatpak run dev.hyprism.HyPrism
  \`\`\`

- **AppImage** (modern systems):
  \`\`\`bash
  chmod +x HyPrism-v${VERSION}-x86_64.AppImage
  ./HyPrism-v${VERSION}-x86_64.AppImage
  \`\`\`

- **Tar.gz** (manual installation):
  \`\`\`bash
  tar -xzf HyPrism-v${VERSION}-linux-x86_64.tar.gz
  ./HyPrism
  \`\`\`

**Windows**
- Download \`HyPrism-v${VERSION}-windows-amd64.zip\` (if available)
- Extract and run \`HyPrism.exe\`

### Changes
<!-- Add your changelog here -->

### Notes
- Flatpak version uses GNOME Runtime 42 for compatibility with older distros
- AppImage requires webkit2gtk-4.1 (use Flatpak for older systems)

**Full Changelog**: https://github.com/yyyumeniku/HyPrism/compare/v${VERSION}...HEAD
EOF
)

# Create the release
echo -e "${YELLOW}Creating GitHub release...${NC}"
gh release create "${TAG}" \
    --title "v${VERSION}" \
    --notes "${RELEASE_NOTES}" \
    releases/*

if [ $? -eq 0 ]; then
    echo -e "${GREEN}╔══════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║       Release Created Successfully!                  ║${NC}"
    echo -e "${GREEN}║       View at: https://github.com/yyyumeniku/HyPrism/releases/tag/${TAG}${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════════════════╝${NC}"
else
    echo -e "${RED}Failed to create release${NC}"
    exit 1
fi
