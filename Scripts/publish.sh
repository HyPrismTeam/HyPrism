#!/usr/bin/env bash
# ============================================================================
# HyPrism Publish Script
# Packages the Sciter-based application for distribution.
#
# Usage:
#   ./Scripts/publish.sh <target> [<target>...] [--arch x64|arm64]
#
# Targets:
#   all        All formats for the current platform
#   linux      All Linux formats (AppImage + deb + rpm + tar.xz)
#   win        All Windows formats (zip + exe)
#   mac        All macOS formats (dmg)
#   appimage   Linux AppImage
#   deb        Linux .deb package
#   rpm        Linux .rpm package
#   tar        Linux .tar.xz archive
#   flatpak    Linux .flatpak bundle
#   dmg        macOS .dmg disk image
#   zip        Windows / Linux portable .zip
#   exe        Windows installer .exe (NSIS)
#   clean      Remove dist/ and intermediate publish dirs
#
# Options:
#   --arch <arch>   Build only for specific architecture (x64 or arm64)
#
# Platform restrictions:
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
LINUX_APP_ID="io.github.HyPrismTeam.HyPrism"
APP_NAME="HyPrism"
APP_BINARY="HyPrism"
MAINTAINER="HyPrism Team <whoisfreak@icloud.com>"
DESCRIPTION="Cross-platform Hytale launcher"
LONG_DESCRIPTION="HyPrism is a cross-platform Hytale launcher with instance and mod management."
CATEGORIES="Game;Utility;"

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

# ─── Detect current OS ───────────────────────────────────────────────────────
detect_os() {
    case "$(uname -s)" in
        Linux*)             echo "linux" ;;
        Darwin*)            echo "mac" ;;
        MINGW*|MSYS*|CYGWIN*) echo "win" ;;
        *)                  echo "unknown" ;;
    esac
}

CURRENT_OS="$(detect_os)"

# ─── Parse arguments ─────────────────────────────────────────────────────────
TARGETS=()
ARCH_FILTER=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch)
            [[ -z "${2:-}" ]] && { log_error "--arch requires an argument (x64 or arm64)"; exit 1; }
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
    echo "Usage: $0 <target> [<target>...] [--arch x64|arm64]"
    echo "Targets: all linux win mac appimage deb rpm tar flatpak dmg zip exe clean"
    exit 1
fi

# ─── Counters ────────────────────────────────────────────────────────────────
BUILD_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0

# ─── Architecture helpers ────────────────────────────────────────────────────
get_arches() {
    local platform="$1"
    local arches=""
    case "$platform" in
        win)   arches="x64" ;;
        linux) arches="x64 arm64" ;;
        mac)   arches="arm64" ;;
        *)     arches="x64" ;;
    esac
    if [[ -n "$ARCH_FILTER" ]]; then
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

get_rid() {
    local platform="$1" arch="$2"
    case "${platform}-${arch}" in
        linux-x64)   echo "linux-x64" ;;
        linux-arm64) echo "linux-arm64" ;;
        win-x64)     echo "win-x64" ;;
        win-arm64)   echo "win-arm64" ;;
        mac-x64)     echo "osx-x64" ;;
        mac-arm64)   echo "osx-arm64" ;;
        *) log_error "Unknown platform-arch: ${platform}-${arch}"; exit 1 ;;
    esac
}

arch_to_deb_arch() {
    case "$1" in x64) echo "amd64" ;; arm64) echo "arm64" ;; *) echo "$1" ;; esac
}
arch_to_rpm_arch() {
    case "$1" in x64) echo "x86_64" ;; arm64) echo "aarch64" ;; *) echo "$1" ;; esac
}
arch_to_flatpak_arch() {
    case "$1" in x64) echo "x86_64" ;; arm64) echo "aarch64" ;; *) echo "$1" ;; esac
}

check_platform() { [[ "$CURRENT_OS" == "$1" ]]; }

platform_name() {
    case "$1" in linux) echo "Linux" ;; win) echo "Windows" ;; mac) echo "macOS" ;; *) echo "$1" ;; esac
}

# ─── Version ─────────────────────────────────────────────────────────────────
get_version() {
    grep -oP '<Version>\K[^<]+' "$PROJECT_ROOT/HyPrism.csproj" 2>/dev/null || echo "0.0.0"
}

# ─── Icon helpers ─────────────────────────────────────────────────────────────
get_icon_png() {
    local src="$PROJECT_ROOT/Frontend/public/icon.png"
    [[ -f "$src" ]] && echo "$src" && return
    local fb="$PROJECT_ROOT/Build/icon.png"
    [[ -f "$fb" ]] && echo "$fb" && return
    echo ""
}

prepare_linux_icons() {
    local icon_src
    icon_src="$(get_icon_png)"
    [[ -z "$icon_src" ]] && { log_warn "No source icon found"; return 0; }

    local iconset_root="$PROJECT_ROOT/Build/icons"
    mkdir -p "$PROJECT_ROOT/Build"
    cp "$icon_src" "$PROJECT_ROOT/Build/icon.png"
    rm -rf "$iconset_root" && mkdir -p "$iconset_root"

    for size in 16 24 32 48 64 128 256 512; do
        local hicolor="$iconset_root/hicolor/${size}x${size}/apps"
        mkdir -p "$hicolor"
        if command -v magick >/dev/null 2>&1; then
            magick "$icon_src" -resize "${size}x${size}" "$iconset_root/${size}x${size}.png" 2>/dev/null || cp "$icon_src" "$iconset_root/${size}x${size}.png"
        elif command -v convert >/dev/null 2>&1; then
            convert "$icon_src" -resize "${size}x${size}" "$iconset_root/${size}x${size}.png" 2>/dev/null || cp "$icon_src" "$iconset_root/${size}x${size}.png"
        else
            cp "$icon_src" "$iconset_root/${size}x${size}.png"
        fi
        cp "$iconset_root/${size}x${size}.png" "$hicolor/${LINUX_APP_ID}.png"
    done
    cp "$icon_src" "$iconset_root/icon.png"
    log_ok "Prepared Linux icon set in Build/icons"
}

prepare_macos_icon() {
    local icon_src
    icon_src="$(get_icon_png)"
    [[ -z "$icon_src" ]] && { log_warn "No source icon; skipping macOS icns generation"; return 0; }
    mkdir -p "$PROJECT_ROOT/Build"
    cp "$icon_src" "$PROJECT_ROOT/Build/icon.png"

    if ! command -v iconutil >/dev/null 2>&1 || ! command -v sips >/dev/null 2>&1; then
        log_warn "iconutil/sips not available; skipping icns generation"
        return 0
    fi

    local iconset="$PROJECT_ROOT/Build/icon.iconset"
    rm -rf "$iconset" && mkdir -p "$iconset"
    local sizes=(16 32 32 64 128 256 256 512 512 1024)
    local names=(icon_16x16 icon_16x16@2x icon_32x32 icon_32x32@2x icon_128x128 icon_128x128@2x icon_256x256 icon_256x256@2x icon_512x512 icon_512x512@2x)
    for i in "${!sizes[@]}"; do
        sips -z "${sizes[$i]}" "${sizes[$i]}" "$icon_src" --out "$iconset/${names[$i]}.png" >/dev/null 2>&1 || true
    done
    iconutil -c icns "$iconset" -o "$PROJECT_ROOT/Build/icon.icns" 2>/dev/null && log_ok "Prepared Build/icon.icns" || log_warn "icns generation failed"
    rm -rf "$iconset"
}

# ─── dotnet publish ───────────────────────────────────────────────────────────
do_dotnet_publish() {
    local rid="$1"
    log_info "dotnet publish (RID: $rid)..."
    cd "$PROJECT_ROOT"
    if ! dotnet publish -c Release -p:RuntimeIdentifier="$rid"; then
        log_error "dotnet publish failed for $rid"
        return 1
    fi
    echo "$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"
}

# ─── AppImage ─────────────────────────────────────────────────────────────────
build_appimage() {
    local platform="linux"
    check_platform "$platform" || { log_warn "AppImage requires Linux"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "AppImage"
    prepare_linux_icons

    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    if ! command -v appimagetool >/dev/null 2>&1; then
        log_error "appimagetool not found — install from https://appimage.github.io/appimagetool/"
        FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"

    for arch in $arches; do
        local rid start_time=$SECONDS
        rid="$(get_rid "$platform" "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local appdir="$PROJECT_ROOT/.tmp/AppDir-$arch"
        rm -rf "$appdir"
        mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/metainfo"
        mkdir -p "$appdir/usr/share/icons/hicolor/256x256/apps"

        cp -a "$pub_dir/." "$appdir/usr/bin/"

        # AppRun entry point
        cat > "$appdir/AppRun" << 'APPRUN'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/HyPrism" "$@"
APPRUN
        chmod +x "$appdir/AppRun"

        # Desktop file
        cat > "$appdir/$APP_NAME.desktop" << DESKTOP
[Desktop Entry]
Name=$APP_NAME
Comment=$DESCRIPTION
Exec=HyPrism
Icon=$APP_NAME
Type=Application
Categories=$CATEGORIES
DESKTOP
        cp "$appdir/$APP_NAME.desktop" "$appdir/usr/share/applications/"

        # Icon
        local icon_src; icon_src="$(get_icon_png)"
        if [[ -n "$icon_src" ]]; then
            cp "$icon_src" "$appdir/$APP_NAME.png"
            cp "$icon_src" "$appdir/usr/share/icons/hicolor/256x256/apps/$APP_NAME.png"
        fi

        # AppStream metainfo
        local metainfo="$PROJECT_ROOT/Properties/linux/flatpak/$LINUX_APP_ID.metainfo.xml"
        [[ -f "$metainfo" ]] && cp "$metainfo" "$appdir/usr/share/metainfo/"

        local appimage_arch
        case "$arch" in x64) appimage_arch="x86_64" ;; arm64) appimage_arch="aarch64" ;; *) appimage_arch="$arch" ;; esac

        local out="$DIST_DIR/$APP_NAME-linux-$arch-$version.AppImage"
        ARCH="$appimage_arch" appimagetool "$appdir" "$out" 2>&1 | grep -v "^$" || true

        rm -rf "$appdir"

        local elapsed=$(( SECONDS - start_time ))
        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "AppImage not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── deb ─────────────────────────────────────────────────────────────────────
build_deb() {
    local platform="linux"
    check_platform "$platform" || { log_warn "deb requires Linux"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "deb"

    if ! command -v dpkg-deb >/dev/null 2>&1; then
        log_error "dpkg-deb not found"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"
    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS deb_arch
        rid="$(get_rid "$platform" "$arch")"
        deb_arch="$(arch_to_deb_arch "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local pkg_dir="$PROJECT_ROOT/.tmp/deb-$arch"
        local install_dir="$pkg_dir/usr/lib/hyprism"
        rm -rf "$pkg_dir"
        mkdir -p "$install_dir" "$pkg_dir/DEBIAN"
        mkdir -p "$pkg_dir/usr/bin"
        mkdir -p "$pkg_dir/usr/share/applications"
        mkdir -p "$pkg_dir/usr/share/metainfo"
        mkdir -p "$pkg_dir/usr/share/icons/hicolor/256x256/apps"

        cp -a "$pub_dir/." "$install_dir/"
        chmod +x "$install_dir/$APP_BINARY" 2>/dev/null || true

        # Wrapper in /usr/bin
        cat > "$pkg_dir/usr/bin/hyprism" << WRAPPER
#!/usr/bin/env bash
exec /usr/lib/hyprism/$APP_BINARY "\$@"
WRAPPER
        chmod +x "$pkg_dir/usr/bin/hyprism"

        # Desktop entry
        cat > "$pkg_dir/usr/share/applications/$LINUX_APP_ID.desktop" << DESKTOP
[Desktop Entry]
Name=$APP_NAME
Comment=$DESCRIPTION
Exec=hyprism
Icon=$LINUX_APP_ID
Type=Application
Categories=$CATEGORIES
DESKTOP

        # Icon
        local icon_src; icon_src="$(get_icon_png)"
        [[ -n "$icon_src" ]] && cp "$icon_src" "$pkg_dir/usr/share/icons/hicolor/256x256/apps/$LINUX_APP_ID.png"

        # AppStream metainfo
        local metainfo="$PROJECT_ROOT/Properties/linux/flatpak/$LINUX_APP_ID.metainfo.xml"
        [[ -f "$metainfo" ]] && cp "$metainfo" "$pkg_dir/usr/share/metainfo/"

        # Package size (in KB)
        local installed_size; installed_size=$(du -sk "$pkg_dir" | cut -f1)

        cat > "$pkg_dir/DEBIAN/control" << CONTROL
Package: hyprism
Version: $version
Architecture: $deb_arch
Maintainer: $MAINTAINER
Installed-Size: $installed_size
Depends: libgtk-3-0, libglib2.0-0
Description: $DESCRIPTION
 $LONG_DESCRIPTION
CONTROL

        local out="$DIST_DIR/$APP_NAME-linux-$arch-$version.deb"
        dpkg-deb -b "$pkg_dir" "$out" >/dev/null 2>&1
        rm -rf "$pkg_dir"

        local elapsed=$(( SECONDS - start_time ))
        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "deb not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── rpm ─────────────────────────────────────────────────────────────────────
build_rpm() {
    local platform="linux"
    check_platform "$platform" || { log_warn "rpm requires Linux"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "rpm"

    if ! command -v rpmbuild >/dev/null 2>&1; then
        log_error "rpmbuild not found (install rpm-build)"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"
    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS rpm_arch
        rid="$(get_rid "$platform" "$arch")"
        rpm_arch="$(arch_to_rpm_arch "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local build_root="$PROJECT_ROOT/.tmp/rpmbuild-$arch"
        local install_dir="$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/lib/hyprism"
        rm -rf "$build_root"
        mkdir -p "$install_dir"
        mkdir -p "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/bin"
        mkdir -p "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/share/applications"
        mkdir -p "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/share/icons/hicolor/256x256/apps"
        mkdir -p "$build_root"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

        cp -a "$pub_dir/." "$install_dir/"
        chmod +x "$install_dir/$APP_BINARY" 2>/dev/null || true

        cat > "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/bin/hyprism" << 'WRAPPER'
#!/usr/bin/env bash
exec /usr/lib/hyprism/HyPrism "$@"
WRAPPER
        chmod +x "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/bin/hyprism"

        cat > "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/share/applications/$LINUX_APP_ID.desktop" << DESKTOP
[Desktop Entry]
Name=$APP_NAME
Comment=$DESCRIPTION
Exec=hyprism
Icon=$LINUX_APP_ID
Type=Application
Categories=$CATEGORIES
DESKTOP

        local icon_src; icon_src="$(get_icon_png)"
        [[ -n "$icon_src" ]] && cp "$icon_src" "$build_root/BUILDROOT/hyprism-$version-1.$rpm_arch/usr/share/icons/hicolor/256x256/apps/$LINUX_APP_ID.png"

        cat > "$build_root/SPECS/hyprism.spec" << SPEC
Name: hyprism
Version: $version
Release: 1
Summary: $DESCRIPTION
License: GPL-3.0-only
BuildArch: $rpm_arch
AutoReqProv: no
Requires: gtk3, glib2

%description
$LONG_DESCRIPTION

%install

%files
/usr/lib/hyprism/
/usr/bin/hyprism
/usr/share/applications/$LINUX_APP_ID.desktop
/usr/share/icons/hicolor/256x256/apps/$LINUX_APP_ID.png

%post
update-desktop-database -q /usr/share/applications || true
gtk-update-icon-cache -q /usr/share/icons/hicolor || true
SPEC

        rpmbuild --define "_topdir $build_root" \
                 --define "_builddir $build_root/BUILD" \
                 --define "_buildrootdir $build_root/BUILDROOT" \
                 --define "_rpmdir $build_root/RPMS" \
                 --nodebuginfo -bb "$build_root/SPECS/hyprism.spec" >/dev/null 2>&1

        local built_rpm; built_rpm="$(find "$build_root/RPMS" -type f -name "*.rpm" | head -1)"
        local out="$DIST_DIR/$APP_NAME-linux-$arch-$version.rpm"

        rm -rf "$build_root"
        local elapsed=$(( SECONDS - start_time ))

        if [[ -n "$built_rpm" && -f "$built_rpm" ]]; then
            mv "$built_rpm" "$out"
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "rpm not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── tar.xz ──────────────────────────────────────────────────────────────────
build_tar() {
    local platform="linux"
    check_platform "$platform" || { log_warn "tar.xz requires Linux"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "tar.xz"

    local version; version="$(get_version)"
    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS
        rid="$(get_rid "$platform" "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local out="$DIST_DIR/$APP_NAME-linux-$arch-$version.tar.xz"
        tar -cJf "$out" -C "$(dirname "$pub_dir")" "$(basename "$pub_dir")" --transform "s|$(basename "$pub_dir")|hyprism-$version|"

        local elapsed=$(( SECONDS - start_time ))
        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "tar.xz not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── flatpak ─────────────────────────────────────────────────────────────────
build_flatpak() {
    local platform="linux"
    check_platform "$platform" || { log_warn "flatpak requires Linux"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "flatpak"

    if ! command -v flatpak-builder >/dev/null 2>&1; then
        log_error "flatpak-builder not found"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"
    local manifest_dir="$PROJECT_ROOT/Properties/linux/flatpak"
    local manifest="$manifest_dir/$LINUX_APP_ID.yml"

    if [[ ! -f "$manifest" ]]; then
        log_error "Flatpak manifest not found: $manifest"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS flatpak_arch
        rid="$(get_rid "$platform" "$arch")"
        flatpak_arch="$(arch_to_flatpak_arch "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        # Copy publish output into bundle/ dir expected by the manifest
        local bundle_dir="$manifest_dir/bundle"
        rm -rf "$bundle_dir" && mkdir -p "$bundle_dir"
        cp -a "$pub_dir/." "$bundle_dir/"

        local repo_dir="$DIST_DIR/flatpak-repo-$arch"
        local build_dir="$PROJECT_ROOT/.flatpak-builder/build-$arch"
        rm -rf "$repo_dir" "$build_dir"
        mkdir -p "$repo_dir"

        if ! (cd "$manifest_dir" && flatpak-builder \
                --force-clean \
                --repo="$repo_dir" \
                --install-deps-from=flathub \
                "--arch=$flatpak_arch" \
                "$build_dir" "$manifest"); then
            log_error "flatpak-builder failed for $arch"
            rm -rf "$bundle_dir" "$build_dir"
            FAIL_COUNT=$((FAIL_COUNT+1)); continue
        fi

        local flatpak_app_id
        flatpak_app_id="$(grep -E '^app-id:' "$manifest" | awk '{print $2}' | tr -d "\"'")" || flatpak_app_id="$LINUX_APP_ID"

        local out="$DIST_DIR/$APP_NAME-linux-$arch-$version.flatpak"
        flatpak build-bundle "$repo_dir" "$out" "$flatpak_app_id" "--arch=$flatpak_arch"

        rm -rf "$bundle_dir" "$build_dir"
        local elapsed=$(( SECONDS - start_time ))

        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "flatpak not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── zip ─────────────────────────────────────────────────────────────────────
build_zip() {
    log_section "zip"
    local version; version="$(get_version)"

    local platform
    case "$CURRENT_OS" in linux) platform="linux" ;; win) platform="win" ;; mac) platform="mac" ;; *) platform="linux" ;; esac

    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS
        rid="$(get_rid "$platform" "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local out="$DIST_DIR/$APP_NAME-$platform-$arch-$version.zip"
        (cd "$(dirname "$pub_dir")" && zip -qr "$out" "$(basename "$pub_dir")")

        local elapsed=$(( SECONDS - start_time ))
        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "zip not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── exe (Windows NSIS installer) ────────────────────────────────────────────
build_exe() {
    local platform="win"
    check_platform "$platform" || { log_warn "exe (NSIS) requires Windows"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "exe (NSIS)"

    if ! command -v makensis >/dev/null 2>&1; then
        log_error "makensis not found (install NSIS)"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"
    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS
        rid="$(get_rid "$platform" "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        local nsis_script="$PROJECT_ROOT/.tmp/installer-$arch.nsi"
        local out="$DIST_DIR/$APP_NAME-win-$arch-$version.exe"

        cat > "$nsis_script" << NSIS
!define AppName "$APP_NAME"
!define AppVersion "$version"
!define OutFile "$out"
!define InstDir "\$PROGRAMFILES64\\$APP_NAME"
!define SourceDir "$pub_dir"

Name "\${AppName}"
OutFile "\${OutFile}"
InstallDir "\${InstDir}"
RequestExecutionLevel admin

Section "Install"
    SetOutPath "\${InstDir}"
    File /r "\${SourceDir}\\*.*"
    CreateShortCut "\$DESKTOP\\$APP_NAME.lnk" "\${InstDir}\\$APP_BINARY.exe"
    CreateDirectory "\$SMPROGRAMS\\$APP_NAME"
    CreateShortCut "\$SMPROGRAMS\\$APP_NAME\\$APP_NAME.lnk" "\${InstDir}\\$APP_BINARY.exe"
SectionEnd

Section "Uninstall"
    Delete "\$DESKTOP\\$APP_NAME.lnk"
    Delete "\$SMPROGRAMS\\$APP_NAME\\$APP_NAME.lnk"
    RMDir "\$SMPROGRAMS\\$APP_NAME"
    RMDir /r "\${InstDir}"
SectionEnd
NSIS

        makensis "$nsis_script" >/dev/null 2>&1
        rm -f "$nsis_script"

        local elapsed=$(( SECONDS - start_time ))
        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "exe not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── dmg (macOS) ─────────────────────────────────────────────────────────────
build_dmg() {
    local platform="mac"
    check_platform "$platform" || { log_warn "dmg requires macOS"; SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }
    log_section "dmg"
    prepare_macos_icon

    if ! command -v hdiutil >/dev/null 2>&1; then
        log_error "hdiutil not found"; FAIL_COUNT=$((FAIL_COUNT+1)); return 1
    fi

    local version; version="$(get_version)"
    local arches; arches="$(get_arches "$platform")"
    [[ -z "$arches" ]] && { SKIP_COUNT=$((SKIP_COUNT+1)); return 0; }

    for arch in $arches; do
        local rid start_time=$SECONDS
        rid="$(get_rid "$platform" "$arch")"
        local pub_dir; pub_dir="$(do_dotnet_publish "$rid")" || { FAIL_COUNT=$((FAIL_COUNT+1)); continue; }

        # Build .app bundle
        local app_bundle="$PROJECT_ROOT/.tmp/$APP_NAME.app"
        rm -rf "$app_bundle"
        mkdir -p "$app_bundle/Contents/MacOS" "$app_bundle/Contents/Resources"
        cp -a "$pub_dir/." "$app_bundle/Contents/MacOS/"
        chmod +x "$app_bundle/Contents/MacOS/$APP_BINARY" 2>/dev/null || true

        local icns="$PROJECT_ROOT/Build/icon.icns"
        [[ -f "$icns" ]] && cp "$icns" "$app_bundle/Contents/Resources/$APP_NAME.icns"

        local info_plist="$PROJECT_ROOT/Properties/macos/Info.plist"
        if [[ -f "$info_plist" ]]; then
            cp "$info_plist" "$app_bundle/Contents/Info.plist"
        else
            cat > "$app_bundle/Contents/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>$APP_BINARY</string>
    <key>CFBundleIdentifier</key><string>$LINUX_APP_ID</string>
    <key>CFBundleName</key><string>$APP_NAME</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleIconFile</key><string>$APP_NAME</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
        fi

        # Stage for DMG
        local dmg_stage="$PROJECT_ROOT/.tmp/dmg-$arch"
        rm -rf "$dmg_stage" && mkdir -p "$dmg_stage"
        cp -r "$app_bundle" "$dmg_stage/"
        ln -s /Applications "$dmg_stage/Applications" 2>/dev/null || true

        local out="$DIST_DIR/$APP_NAME-mac-$arch-$version.dmg"
        hdiutil create -volname "$APP_NAME" -srcfolder "$dmg_stage" -ov -format UDZO "$out" >/dev/null 2>&1

        rm -rf "$app_bundle" "$dmg_stage"
        local elapsed=$(( SECONDS - start_time ))

        if [[ -f "$out" ]]; then
            log_ok "$(basename "$out") ($(du -h "$out" | cut -f1)) — ${elapsed}s"
            BUILD_COUNT=$((BUILD_COUNT+1))
        else
            log_error "dmg not produced for $arch"; FAIL_COUNT=$((FAIL_COUNT+1))
        fi
    done
}

# ─── Compound targets ─────────────────────────────────────────────────────────
build_linux() {
    build_appimage
    build_deb
    build_rpm
    build_tar
}

build_win() {
    build_zip
    build_exe
}

build_mac() {
    build_dmg
}

build_all() {
    build_linux
    build_win
    build_mac
}

# ─── Clean ────────────────────────────────────────────────────────────────────
do_clean() {
    log_section "Cleaning"
    rm -rf "$DIST_DIR" && log_ok "Removed dist/"
    rm -rf "$PROJECT_ROOT/.tmp" && log_ok "Removed .tmp/"
    rm -rf "$PROJECT_ROOT/.flatpak-builder" && log_ok "Removed .flatpak-builder/"
    for rid in linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64; do
        local p="$PROJECT_ROOT/bin/Release/net10.0/$rid/publish"
        [[ -d "$p" ]] && rm -rf "$p" && log_ok "Removed bin/.../$rid/publish"
    done
    log_ok "Clean complete"
}

# ─── Main ─────────────────────────────────────────────────────────────────────
if [[ "${TARGETS[0]}" == "clean" ]]; then
    do_clean; exit 0
fi

mkdir -p "$DIST_DIR" "$PROJECT_ROOT/.tmp"
TOTAL_START=$SECONDS

log_section "HyPrism Publish"
log_info "OS: $(platform_name "$CURRENT_OS")"
log_info "Version: $(get_version)"
log_info "Targets: ${TARGETS[*]}"
[[ -n "$ARCH_FILTER" ]] && log_info "Arch: $ARCH_FILTER" || log_info "Arch: x64 + arm64"
log_info "Output: $DIST_DIR/"

for target in "${TARGETS[@]}"; do
    case "$target" in
        all)      build_all ;;
        linux)    build_linux ;;
        win)      build_win ;;
        mac)      build_mac ;;
        appimage) build_appimage ;;
        deb)      build_deb ;;
        rpm)      build_rpm ;;
        tar)      build_tar ;;
        flatpak)  build_flatpak ;;
        dmg)      build_dmg ;;
        zip)      build_zip ;;
        exe)      build_exe ;;
        *)
            log_error "Unknown target: $target"
            echo "Valid targets: all linux win mac appimage deb rpm tar flatpak dmg zip exe clean"
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
