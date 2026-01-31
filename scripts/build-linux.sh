#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${VERSION:-2.0.0}"
ARTIFACTS="$ROOT/artifacts"
RIDS=("win-x64" "linux-x64" "osx-arm64")
AUTO_INSTALL="${AUTO_INSTALL:-0}"
REMOTE_LINUX_HOST="${REMOTE_LINUX_HOST:-${REMOTE_HOST:-}}"
REMOTE_LINUX_PATH="${REMOTE_LINUX_PATH:-${REMOTE_PATH:-~/HyPrism}}"
SKIP_REMOTE="${SKIP_REMOTE:-0}"
ONLY_BUNDLE=0
SKIP_BUILD="${SKIP_BUILD:-0}"

show_help() {
  cat <<'EOF'
Usage: ./$0 [options]

Options:
  --help                Show this help message and exit
  --auto-install-deps   Auto-install missing dependencies (Ubuntu/Fedora)
  --only-bundle         Build only the bundle (skip all packaging)
  --no-appimage         Skip AppImage packaging
  --no-flatpak          Skip Flatpak packaging
  --no-deb-rpm          Skip deb/rpm packaging
  --only-appimage       Build only AppImage (skip deb/rpm and Flatpak)
  --only-flatpak        Build only Flatpak (skip deb/rpm and AppImage)
  --only-deb-rpm        Build only deb/rpm (skip Flatpak and AppImage)
EOF
}

# Packaging flags (Linux-only packaging step)
DO_APPIMAGE=1
DO_FLATPAK=1
DO_DEB=1

for arg in "$@"; do
  case "$arg" in
    --help|-h)
      show_help
      exit 0
      ;;
    --auto-install-deps)
      AUTO_INSTALL=1
      ;;
    --only-bundle)
      ONLY_BUNDLE=1
      DO_APPIMAGE=0
      DO_FLATPAK=0
      DO_DEB=0
      ;;
    --no-appimage)
      DO_APPIMAGE=0
      ;;
    --no-flatpak)
      DO_FLATPAK=0
      ;;
    --no-deb)
      DO_DEB=0
      ;;
    --only-appimage)
      DO_APPIMAGE=1
      DO_FLATPAK=0
      DO_DEB=0
      ;;
    --only-flatpak)
      DO_APPIMAGE=0
      DO_FLATPAK=1
      DO_DEB=0
      ;;
    --only-deb-rpm)
      DO_APPIMAGE=0
      DO_FLATPAK=0
      DO_DEB=1
      ;;
    *)
      ;;
  esac
done

if [[ "$ONLY_BUNDLE" == "1" ]]; then
  RIDS=("linux-x64" "linux-arm64")
elif [[ "$DO_APPIMAGE" == "1" && "$DO_FLATPAK" == "0" && "$DO_DEB" == "0" ]]; then
  RIDS=("linux-x64")
elif [[ "$DO_FLATPAK" == "1" && "$DO_APPIMAGE" == "0" && "$DO_DEB" == "0" ]]; then
  RIDS=("linux-x64")
elif [[ "$DO_DEB" == "1" && "$DO_APPIMAGE" == "0" && "$DO_FLATPAK" == "0" ]]; then
  RIDS=("linux-x64")
fi

if [[ "$(uname -s)" == "Linux" ]]; then
  detect_distro() {
    if [[ -f /etc/os-release ]]; then
      . /etc/os-release
      echo "${ID:-}"
      return 0
    fi
    echo ""
  }

  install_packages() {
    local ubuntu_pkgs="$1"
    local fedora_pkgs="$2"
    local distro
    distro="$(detect_distro)"

    case "$distro" in
      ubuntu|debian)
        # Non-interactive installs for CI / automation; avoid tzdata and config prompts
        sudo DEBIAN_FRONTEND=noninteractive apt-get update -y
        sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -o Dpkg::Options::="--force-confdef" -o Dpkg::Options::="--force-confold" $ubuntu_pkgs || true
        ;;
      fedora)
        # Use -y to avoid interactive prompts
        sudo dnf -y install $fedora_pkgs || true
        ;;
      *)
        return 1
        ;;
    esac
  }

  ensure_tool() {
    local cmd="$1" ubuntu_pkgs="$2" fedora_pkgs="$3" post_install="$4"
    if command -v "$cmd" >/dev/null 2>&1; then
      return 0
    fi

    if [[ "$AUTO_INSTALL" == "1" ]]; then
      echo "==> Installing dependencies for $cmd"
      if ! install_packages "$ubuntu_pkgs" "$fedora_pkgs"; then
        echo "!! Unsupported distro for auto-install. Please install: $ubuntu_pkgs / $fedora_pkgs"
      fi
      if [[ -n "$post_install" ]]; then
        eval "$post_install" || true
      fi
    else
      echo "!! Missing $cmd. Install: $ubuntu_pkgs (Ubuntu) or $fedora_pkgs (Fedora). Use --auto-install-deps to auto-install."
    fi
    return 1
  }

  require_tool() {
    local cmd="$1" ubuntu_pkgs="$2" fedora_pkgs="$3" post_install="$4"
    ensure_tool "$cmd" "$ubuntu_pkgs" "$fedora_pkgs" "$post_install" || true
    if ! command -v "$cmd" >/dev/null 2>&1; then
      echo "ERROR: $cmd is required for this build but is not available." >&2
      exit 1
    fi
  }

  # Packaging helpers often missing on fresh VMs
  if [[ "$ONLY_BUNDLE" != "1" ]]; then
    if [[ "$DO_DEB" == "1" ]]; then
      require_tool fpm "ruby ruby-dev rubygems build-essential rpm" "ruby ruby-devel rubygems @development-tools rpm-build" "command -v fpm >/dev/null 2>&1 || sudo gem install --no-document fpm"
    fi
    if [[ "$DO_APPIMAGE" == "1" ]]; then
      require_tool appimagetool "appimagetool" "appimagetool" "command -v appimagetool >/dev/null 2>&1 || { sudo apt-get update -y || true; sudo apt-get install -y libfuse2 || true; curl -fsSL -o /tmp/appimagetool \"https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage\"; chmod +x /tmp/appimagetool; sudo mv /tmp/appimagetool /usr/local/bin/appimagetool; }"
    fi
    if [[ "$DO_FLATPAK" == "1" ]]; then
      require_tool flatpak-builder "flatpak flatpak-builder" "flatpak flatpak-builder" ""
      # appstream-compose is required by flatpak-builder for AppStream metadata
      require_tool appstream-compose "appstream-util appstream desktop-file-utils" "appstream desktop-file-utils" ""
    fi
  fi
fi

mkdir -p "$ARTIFACTS"

if [[ "$SKIP_BUILD" == "1" ]]; then
  echo "==> Skipping frontend + publish (SKIP_BUILD=1)"
  if [[ -d "$ROOT/packaging/flatpak/bundle" && ! -d "$ARTIFACTS/linux-x64/portable" ]]; then
    mkdir -p "$ARTIFACTS/linux-x64/portable"
    cp -R "$ROOT/packaging/flatpak/bundle"/* "$ARTIFACTS/linux-x64/portable/" || true
  fi

  if [[ ! -f "$ARTIFACTS/linux-x64/portable/HyPrism" ]]; then
    echo "ERROR: artifacts/linux-x64/portable/HyPrism is missing. Run --only-bundle first or provide bundle." >&2
    exit 1
  fi
else
  echo "==> Building frontend"
  if [[ -f "$ROOT/frontend/package-lock.json" ]]; then
    # Use CI mode and disable audit/funding prompts to be non-interactive
    (cd "$ROOT/frontend" && CI=1 npm ci --no-audit --no-fund --silent)
  else
    (cd "$ROOT/frontend" && CI=1 npm install --no-audit --no-fund --silent)
  fi
  # Force non-interactive build and minimal output
  (cd "$ROOT/frontend" && CI=1 npm run build --silent)

  echo "==> Restoring and publishing backend"
  dotnet restore "$ROOT/HyPrism.csproj"

  for rid in "${RIDS[@]}"; do
    out="$ARTIFACTS/$rid/portable"
    echo "--> Publishing $rid to $out"
    dotnet publish "$ROOT/HyPrism.csproj" -c Release -r "$rid" --self-contained true \
      /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:IncludeNativeLibrariesForSelfExtract=true \
      -o "$out"

    case "$rid" in
      win-*)
        (cd "$out" && zip -9 -r "$ARTIFACTS/HyPrism-${rid}-portable.zip" .)
        ;;
      osx-*)
        (cd "$out" && zip -9 -r "$ARTIFACTS/HyPrism-${rid}-portable.zip" .)
        ;;
      linux-*)
        tar -czf "$ARTIFACTS/HyPrism-${rid}-portable.tar.gz" -C "$out" .
        ;;
    esac
  done
fi

if [[ "$ONLY_BUNDLE" == "1" && "$(uname -s)" == "Linux" ]]; then
  echo "==> Preparing bundle only"
  LINUX_OUT="$ARTIFACTS/linux-x64/portable"
  rm -rf "$ROOT/packaging/flatpak/bundle"
  mkdir -p "$ROOT/packaging/flatpak/bundle"
  cp -R "$LINUX_OUT"/* "$ROOT/packaging/flatpak/bundle/" || true
  cp "$ROOT/packaging/flatpak/dev.hyprism.HyPrism."* "$ROOT/packaging/flatpak/bundle/" || true
  chmod +x "$ROOT/packaging/flatpak/bundle/HyPrism" || true
  echo "Bundle ready at $ROOT/packaging/flatpak/bundle"
  echo "Done. Artifacts in $ARTIFACTS"
  exit 0
fi

# Linux-only packaging helpers
if [[ "$(uname -s)" == "Linux" ]]; then
  LINUX_OUT="$ARTIFACTS/linux-x64/portable"
  PKGROOT="$ARTIFACTS/linux-x64/pkgroot"
  ICON_SRC="$ROOT/packaging/flatpak/dev.hyprism.HyPrism.png"
  DESKTOP_SRC="$ROOT/packaging/flatpak/dev.hyprism.HyPrism.desktop"

  echo "==> Staging Linux package tree"
  rm -rf "$PKGROOT"
  mkdir -p "$PKGROOT/opt/hyprism" "$PKGROOT/usr/bin" "$PKGROOT/usr/share/applications" "$PKGROOT/usr/share/icons/hicolor/256x256/apps"
  cp -R "$LINUX_OUT"/* "$PKGROOT/opt/hyprism/"
  ln -sf /opt/hyprism/HyPrism "$PKGROOT/usr/bin/hyprism"
  [[ -f "$DESKTOP_SRC" ]] && cp "$DESKTOP_SRC" "$PKGROOT/usr/share/applications/dev.hyprism.HyPrism.desktop"
  [[ -f "$ICON_SRC" ]] && cp "$ICON_SRC" "$PKGROOT/usr/share/icons/hicolor/256x256/apps/dev.hyprism.HyPrism.png"

  if [[ "$DO_DEB" == "1" ]]; then
    echo "==> Building .deb and .rpm via fpm"
    fpm -s dir -t deb -n hyprism -v "$VERSION" -C "$PKGROOT" \
      --description "HyPrism launcher" --url "https://github.com/yyyumeniku/HyPrism" \
      -p "$ARTIFACTS/HyPrism-linux-x64.deb" .
    fpm -s dir -t rpm -n hyprism -v "$VERSION" -C "$PKGROOT" \
      --description "HyPrism launcher" --url "https://github.com/yyyumeniku/HyPrism" \
      -p "$ARTIFACTS/HyPrism-linux-x64.rpm" .
  else
    echo "==> Skipping deb/rpm generation (--no-deb/--only-*)"
  fi

  if [[ "$DO_APPIMAGE" == "1" ]]; then
    echo "==> Building AppImage"
    APPDIR="$ARTIFACTS/linux-x64/AppDir"
    rm -rf "$APPDIR"
    mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
    cp "$LINUX_OUT/HyPrism" "$APPDIR/usr/bin/HyPrism"
    chmod +x "$APPDIR/usr/bin/HyPrism"
    [[ -f "$DESKTOP_SRC" ]] && cp "$DESKTOP_SRC" "$APPDIR/usr/share/applications/dev.hyprism.HyPrism.desktop"
    [[ -f "$ICON_SRC" ]] && cp "$ICON_SRC" "$APPDIR/usr/share/icons/hicolor/256x256/apps/dev.hyprism.HyPrism.png"
    # appimagetool expects the icon at AppDir root when referenced in desktop file
    [[ -f "$ICON_SRC" ]] && cp "$ICON_SRC" "$APPDIR/dev.hyprism.HyPrism.png"
    cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
exec "$(dirname "$0")/usr/bin/HyPrism" "$@"
EOF
    chmod +x "$APPDIR/AppRun"
    (cd "$APPDIR" && ln -sf usr/share/applications/dev.hyprism.HyPrism.desktop HyPrism.desktop)
    # Remove existing artifact to avoid interactive overwrite prompts
    rm -f "$ARTIFACTS/HyPrism-linux-x64.AppImage"
    appimagetool "$APPDIR" "$ARTIFACTS/HyPrism-linux-x64.AppImage"
  else
    echo "==> Skipping AppImage (--no-appimage/--only-*)"
  fi

  if [[ "$DO_FLATPAK" == "1" ]]; then
    echo "==> Building Flatpak"
    FLATPAK_STAGE="$ARTIFACTS/linux-x64/flatpak-build"
    FLATPAK_REPO="$ARTIFACTS/flatpak-repo"
    # Use user remotes to avoid system-remote lookups in CI
    flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
    flatpak remote-add --user --if-not-exists flathub-beta https://flathub.org/beta-repo/flathub-beta.flatpakrepo
    rm -rf "$ROOT/packaging/flatpak/bundle"
    mkdir -p "$ROOT/packaging/flatpak/bundle"
    cp -R "$LINUX_OUT"/* "$ROOT/packaging/flatpak/bundle/"
    cp "$ROOT/packaging/flatpak/dev.hyprism.HyPrism."* "$ROOT/packaging/flatpak/bundle/" || true
    chmod +x "$ROOT/packaging/flatpak/bundle/HyPrism" || true
    flatpak-builder --user --force-clean "$FLATPAK_STAGE" \
      --install-deps-from=flathub --install-deps-from=flathub-beta \
      "$ROOT/packaging/flatpak/dev.hyprism.HyPrism.json" --repo="$FLATPAK_REPO"
    echo "==> Bundling Flatpak"
    rm -f "$ARTIFACTS/HyPrism-linux-x64.flatpak"
    flatpak build-bundle "$FLATPAK_REPO" "$ARTIFACTS/HyPrism-linux-x64.flatpak" dev.hyprism.HyPrism \
      --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo
  else
    echo "==> Skipping Flatpak (--no-flatpak/--only-*)"
  fi
fi

# Optional: trigger remote Linux build (e.g., Parallels VM) after local steps
if [[ -n "$REMOTE_LINUX_HOST" && "$SKIP_REMOTE" != "1" ]]; then
  echo "==> Triggering remote build on $REMOTE_LINUX_HOST ($REMOTE_LINUX_PATH)"
  ssh -o BatchMode=yes "$REMOTE_LINUX_HOST" "cd '$REMOTE_LINUX_PATH' && SKIP_REMOTE=1 ./scripts/build-all.sh --auto-install-deps" || {
    echo "!! Remote build failed on $REMOTE_LINUX_HOST" >&2
    exit 1
  }
fi

echo "Done. Artifacts in $ARTIFACTS"
