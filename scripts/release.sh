#!/bin/bash
# Release script for HyPrism
# Usage: ./scripts/release.sh <version>
# Example: ./scripts/release.sh 1.0.0

set -e

if [ -z "$1" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.0.0"
    exit 1
fi

VERSION=$1
TAG="v${VERSION}"

# Check if tag already exists
if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "Error: Tag $TAG already exists!"
    exit 1
fi

# Check for uncommitted changes
if ! git diff-index --quiet HEAD --; then
    echo "Error: You have uncommitted changes. Please commit or stash them first."
    exit 1
fi

echo "Creating release $TAG..."

# Create and push tag (no need to update wails.json as it doesn't have a version field)
git tag -a "$TAG" -m "Release $VERSION"
git push origin main
git push origin "$TAG"

echo ""
echo "✅ Release $TAG created and pushed!"
echo ""
echo "GitHub Actions will now:"
echo "  1. Build binaries for macOS (Intel & ARM), Windows, and Linux"
echo "  2. Create a GitHub release with all binaries"
echo ""
echo "Check progress at: https://github.com/yyyumeniku/HyPrism/actions"
echo "Release will be available at: https://github.com/yyyumeniku/HyPrism/releases/tag/$TAG"
