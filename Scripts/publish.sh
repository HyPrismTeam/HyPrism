#!/usr/bin/env bash
# ============================================================================
# HyPrism Publish Script
# Packages the Electron.NET application for distribution.
#
# Usage:
#   ./Scripts/publish.sh <target> [<target>...] [--arch x64|arm64]
#
# Targets:
#   all        All formats for the current platform
#   linux      All Linux formats (AppImage + deb + rpm + tar.xz)
#   win        All Windows formats (zip)
#   mac        All macOS formats (dmg)
#   appimage   Linux AppImage
#   deb        Linux .deb package
#   rpm        Linux .rpm package
#   tar        Linux .tar.xz archive
#   flatpak    Linux .flatpak bundle
#   dmg        macOS .dmg disk image
#   zip        Windows portable .zip
#   clean      Remove dist/ and intermediate publish dirs
#
# Options:
#   --arch <arch>   Build only for specific architecture (x64 or arm64)
#                   Note: Not all arches are valid for all platforms
#                   - Windows: x64 only
#                   - Linux: x64 and arm64
#                   - macOS: arm64 only (Apple Silicon)
#
# Platform restrictions (enforced by Electron.NET):
#   Linux targets  → must build on Linux
#   Windows targets → must build on Windows
#   macOS targets   → must build on macOS
#
# Examples:
#   ./Scripts/publish.sh all                  # All formats, both arches
#   ./Scripts/publish.sh linux                # All Linux, x64 + arm64
#   ./Scripts/publish.sh appimage --arch x64  # AppImage x64 only
#   ./Scripts/publish.sh deb rpm              # deb + rpm, both arches
#   ./Scripts/publish.sh clean                # Clean dist/ and build dirs
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"
EB_CONFIG="$PROJECT_ROOT/Properties/electron-builder.json"

# ─── Colors ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

log_info()    { echo -e "${BLUE}▸${NC} $*"; }
log_ok()      { echo -e "${GREEN}✓${NC} $*"; }
log_warn()    { echo -e "${YELLOW}⚠${NC} $*"; }
log_error()   { echo -e "${RED}✗${NC} $*"; }
log_section() { echo -e "\n${BOLD}${CYAN}═══ $* ═══${NC}"; }

# ─── Detect current OS ──────────────────────────────────────────────────────
detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "mac" ;;
        MINGW*|MSYS*|CYGWIN*) echo "win" ;;
        *)       echo "unknown" ;;
    esac
}

CURRENT_OS="$(detect_os)"

# ─── Parse arguments ────────────────────────────────────────────────────────
TARGETS=()
ARCH_FILTER=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch)
            if [[ -z "${2:-}" ]]; then
                log_error "--arch requires an argument (x64 or arm64)"
                exit 1
            fi
            ARCH_FILTER="$2"
            shift 2
            ;;
        --help|-h)
            sed -n '/^# ====/,/^# ====/p' "$0" | grep '^#' | sed 's/^# \?//'
            exit 0
            ;;
        *)
            TARGETS+=("$1")
            shift
            ;;
    esac
done

if [[ ${#TARGETS[@]} -eq 0 ]]; then
    log_error "No targets specified."
    echo ""
    echo "Usage: $0 <target> [<target>...] [--arch x64|arm64]"
    echo ""
    echo "Targets: all linux win mac appimage deb rpm tar flatpak dmg zip clean"
    echo ""
    echo "Examples:"
    echo "  $0 all                  # All formats, both arches"
    echo "  $0 appimage --arch x64  # AppImage x64 only"
    echo "  $0 deb rpm              # deb + rpm, both arches"
    exit 1
fi

# ─── Global variables ────────────────────────────────────────────────────────
BUILD_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0

# ─── Determine architectures to build ────────────────────────────────────────
# Platform restrictions:
#   - Windows: x64 only
#   - Linux: x64 and arm64
#   - macOS: arm64 only (Apple Silicon)
get_arches() {
    local platform="$1"
    local arches=""

    case "$platform" in
        win)
            arches="x64"
            ;;
        linux)
            arches="x64 arm64"
            ;;
        mac)
            arches="arm64"
            ;;
        *)
            arches="x64"
            ;;
    esac

    # Apply user filter if specified
    if [[ -n "$ARCH_FILTER" ]]; then
        # Only return the arch if it's in the allowed list for the platform
        if [[ " $arches " == *" $ARCH_FILTER "* ]]; then
            echo "$ARCH_FILTER"
        else
            log_warn "Arch '$ARCH_FILTER' not supported for $platform (allowed: $arches)"
            echo ""
        fi
    else
        echo "$arches"
    fi
}

# ─── Check platform compatibility ────────────────────────────────────────────
# Returns 0 if compatible, 1 if not
check_platform() {
    local target_platform="$1"
    if [[ "$CURRENT_OS" != "$target_platform" ]]; then
        return 1
    fi
    return 0
}

platform_name() {
    case "$1" in
        linux) echo "Linux" ;;
        win)   echo "Windows" ;;
        mac)   echo "macOS" ;;
        *)     echo "$1" ;;
    esac
}

# ─── Map platform+arch to .NET RuntimeIdentifier ─────────────────────────────
get_rid() {
    local platform="$1"
    local arch="$2"
    case "${platform}-${arch}" in
        linux-x64)   echo "linux-x64" ;;
        linux-arm64) echo "linux-arm64" ;;
        win-x64)     echo "win-x64" ;;
        win-arm64)   echo "win-arm64" ;;
        mac-x64)     echo "osx-x64" ;;
        mac-arm64)   echo "osx-arm64" ;;
        *) log_error "Unknown platform-arch: ${platform}-${arch}"; return 1 ;;
    esac
}

# ─── Write temporary electron-builder config ──────────────────────────────────
write_config() {
    local platform="$1"
    local targets_json="$2"

    local platform_block=""
    case "$platform" in
        linux)
            platform_block=$(cat <<'INNER'
  "linux": {
    "target": TARGETS_PLACEHOLDER,
    "executableArgs": ["--no-sandbox"],
    "category": "Game",
    "icon": "Build/icon.png",
    "maintainer": "HyPrism Team"
  }
INNER
)
            ;;
        win)
            platform_block=$(cat <<'INNER'
  "win": {
    "target": TARGETS_PLACEHOLDER,
    "icon": "Build/icon.ico"
  }
INNER
)
            ;;
        mac)
            platform_block=$(cat <<'INNER'
  "mac": {
    "target": TARGETS_PLACEHOLDER,
    "category": "public.app-category.games",
    "icon": "Build/icon.icns"
  }
INNER
)
            ;;
    esac

    platform_block="${platform_block//TARGETS_PLACEHOLDER/$targets_json}"

    # Add flatpak-specific config when building flatpak target
    local flatpak_block=""
    if [[ "$targets_json" == *'"flatpak"'* ]]; then
        flatpak_block=$(cat <<'FLATPAK'
,
  "flatpak": {
    "runtimeVersion": "25.08",
    "baseVersion": "25.08",
    "useWaylandFlags": true
  }
FLATPAK
)
    fi

    cat > "$EB_CONFIG" <<EOF
{
  "\$schema": "https://raw.githubusercontent.com/electron-userland/electron-builder/refs/heads/master/packages/app-builder-lib/scheme.json",
  "compression": "store",
  "artifactName": "\${productName}-\${os}-\${arch}-\${version}.\${ext}",
$platform_block$flatpak_block
}
EOF
}

# ─── Run dotnet publish for one RID and collect artifacts ─────────────────────
do_publish() {
    local rid="$1"
    local label="$2"
    local start_time=$SECONDS

    log_info "Publishing ${BOLD}$label${NC} (${rid})..."

    # Clean previous intermediate build for this RID
    rm -rf "$PROJECT_ROOT/obj/Release/net10.0/$rid/PubTmp" 2>/dev/null || true

    cd "$PROJECT_ROOT"
    local exit_code=0
    # Use -p: instead of /p: for cross-platform compatibility (MSYS/Git Bash on Windows converts /p: to a path)
    dotnet publish -c Release -p:RuntimeIdentifier="$rid" || exit_code=$?

    if [[ $exit_code -ne 0 ]]; then
        log_error "Build failed for $label ($rid) — exit code $exit_code"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    local publish_dir="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"

    if [[ ! -d "$publish_dir" ]]; then
        log_error "No output directory: $publish_dir"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        return 1
    fi

    # Collect distributable artifacts (exclude unpacked dirs and metadata)
    local count=0
    while IFS= read -r -d '' artifact; do
        cp "$artifact" "$DIST_DIR/"
        count=$((count + 1))
        local size
        size=$(du -h "$artifact" | cut -f1)
        log_ok "  $(basename "$artifact") (${size})"
    done < <(find "$publish_dir" -maxdepth 1 -type f \( \
        -name "*.AppImage" -o \
        -name "*.deb" -o \
        -name "*.rpm" -o \
        -name "*.tar.xz" -o \
        -name "*.flatpak" -o \
        -name "*.dmg" -o \
        -name "*.zip" -o \
        -name "*.exe" \
    \) -print0 2>/dev/null)

    local elapsed=$(( SECONDS - start_time ))
    if [[ $count -gt 0 ]]; then
        log_ok "$label ($rid) — $count artifact(s), ${elapsed}s"
        BUILD_COUNT=$((BUILD_COUNT + count))
    else
        log_warn "$label ($rid) — no artifacts found (${elapsed}s)"
    fi
}

# ─── Build for a platform with multiple arches ────────────────────────────────
build_platform() {
    local platform="$1"
    local targets_json="$2"
    local label="$3"

    # Check platform compatibility
    if ! check_platform "$platform"; then
        local pname
        pname=$(platform_name "$platform")
        log_warn "Skipping $label — requires $pname (current OS: $(platform_name "$CURRENT_OS"))"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    log_section "$label"
    write_config "$platform" "$targets_json"

    local arches
    arches=$(get_arches "$platform")
    if [[ -z "$arches" ]]; then
        log_warn "No valid architectures for $platform"
        SKIP_COUNT=$((SKIP_COUNT + 1))
        return 0
    fi

    for arch in $arches; do
        local rid
        rid=$(get_rid "$platform" "$arch")
        do_publish "$rid" "$label [$arch]"
    done
}

# ─── Target definitions ──────────────────────────────────────────────────────

build_appimage() { build_platform "linux" '["AppImage"]'                           "AppImage"; }
build_deb()      { build_platform "linux" '["deb"]'                                 "deb"; }
build_rpm()      { build_platform "linux" '["rpm"]'                                 "rpm"; }
build_tar()      { build_platform "linux" '["tar.xz"]'                              "tar.xz"; }
build_flatpak()  { build_platform "linux" '["flatpak"]'                              "flatpak"; }
build_dmg()      { build_platform "mac"   '["dmg"]'                                 "dmg"; }
build_zip()      { build_platform "win"   '["zip"]'                                 "zip"; }

build_linux()    { build_platform "linux" '["AppImage", "deb", "rpm", "tar.xz"]'    "Linux (all formats)"; }
build_win()      { build_platform "win"   '["zip"]'                                 "Windows (zip)"; }
build_mac()      { build_platform "mac"   '["dmg"]'                                 "macOS (dmg)"; }

build_all() {
    build_linux
    build_win
    build_mac
}

# ─── Clean ────────────────────────────────────────────────────────────────────
do_clean() {
    log_section "Cleaning"
    rm -rf "$DIST_DIR"
    log_ok "Removed dist/"

    # Clean intermediate publish dirs for all RIDs
    local rids=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)
    for rid in "${rids[@]}"; do
        local pub_tmp="$PROJECT_ROOT/obj/Release/net10.0/$rid/PubTmp"
        local pub_dir="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"
        if [[ -d "$pub_tmp" ]]; then
            rm -rf "$pub_tmp"
            log_ok "Removed obj/.../$rid/PubTmp"
        fi
        if [[ -d "$pub_dir" ]]; then
            rm -rf "$pub_dir"
            log_ok "Removed bin/.../$rid/publish"
        fi
    done
    log_ok "Clean complete"
}

# ─── Main ─────────────────────────────────────────────────────────────────────

# Handle clean separately (no config backup needed)
if [[ "${TARGETS[0]}" == "clean" ]]; then
    do_clean
    exit 0
fi

# Save & restore electron-builder.json
EB_CONFIG_BAK=$(mktemp)
cp "$EB_CONFIG" "$EB_CONFIG_BAK" 2>/dev/null || true
trap 'cp "$EB_CONFIG_BAK" "$EB_CONFIG" 2>/dev/null; rm -f "$EB_CONFIG_BAK"' EXIT

mkdir -p "$DIST_DIR"
TOTAL_START=$SECONDS

log_section "HyPrism Publish"
log_info "OS: $(platform_name "$CURRENT_OS")"
log_info "Targets: ${TARGETS[*]}"
[[ -n "$ARCH_FILTER" ]] && log_info "Arch: $ARCH_FILTER" || log_info "Arch: x64 + arm64"
log_info "Output: $DIST_DIR/"

for target in "${TARGETS[@]}"; do
    case "$target" in
        all)       build_all ;;
        linux)     build_linux ;;
        win)       build_win ;;
        mac)       build_mac ;;
        appimage)  build_appimage ;;
        deb)       build_deb ;;
        rpm)       build_rpm ;;
        tar)       build_tar ;;
        flatpak)   build_flatpak ;;
        dmg)       build_dmg ;;
        zip)       build_zip ;;
        *)
            log_error "Unknown target: $target"
            echo "Valid targets: all linux win mac appimage deb rpm tar flatpak dmg zip clean"
            exit 1
            ;;
    esac
done

TOTAL_ELAPSED=$(( SECONDS - TOTAL_START ))

log_section "Summary"
log_info "Time: ${TOTAL_ELAPSED}s"
[[ $BUILD_COUNT -gt 0 ]] && log_ok "Artifacts: $BUILD_COUNT"
[[ $SKIP_COUNT -gt 0 ]]  && log_warn "Skipped: $SKIP_COUNT (wrong platform)"
[[ $FAIL_COUNT -gt 0 ]]  && log_error "Failed: $FAIL_COUNT"

if [[ $BUILD_COUNT -gt 0 ]]; then
    echo ""
    log_info "Artifacts in ${BOLD}$DIST_DIR/${NC}:"
    ls -lhS "$DIST_DIR/" 2>/dev/null | tail -n +2
fi

[[ $FAIL_COUNT -gt 0 ]] && exit 1
exit 0
