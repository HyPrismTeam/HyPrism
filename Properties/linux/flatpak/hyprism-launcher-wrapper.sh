#!/bin/sh
# Bundled copy of hyprism-launcher-wrapper.sh for flatpak bundle packaging
#!/bin/sh
# HyPrism Flatpak launcher wrapper —
# - if a user‑installed copy exists in $XDG_DATA_HOME/HyPrism, run it
# - otherwise download the Linux release (latest → prerelease), extract to app data dir and run
# - fall back to bundled /app/lib/hyprism/HyPrism if anything fails

set -eu

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/HyPrism"
LOG="$DATA_DIR/wrapper.log"
mkdir -p "$DATA_DIR"

log() { printf "%s %s\n" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$LOG"; }

# Defer checking/running a user-installed launcher until we know the remote latest version.
# This enables automatic updates: if a newer release exists on GitHub the wrapper will
# download and replace the installed copy before launching.
# (The actual check happens after querying GitHub releases.)

# Determine asset name by architecture
case "$(uname -m)" in
  x86_64|amd64) ASSET_RE='HyPrism-linux-x86_64.*\\.tar\\.xz' ;;
  aarch64|arm64) ASSET_RE='HyPrism-linux-arm64.*\\.tar\\.xz' ;;
  *) ASSET_RE='HyPrism-linux-x86_64.*\\.tar\\.xz' ;;
esac

# Helper: get browser_download_url for matching asset from GitHub API JSON
get_asset_url() {
  local json="$1" asset
  asset=$(printf "%s" "$json" | grep -E '"browser_download_url"' | sed -E 's/.*"browser_download_url" *: *"([^"]+)".*/\1/' | grep -E "$ASSET_RE" | head -n1 || true)
  printf "%s" "$asset"
}

# Downloader (curl/wget)
download_file() {
  local url="$1" out="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -L --fail --silent --show-error -o "$out" "$url"
    return $?
  elif command -v wget >/dev/null 2>&1; then
    wget -qO "$out" "$url"
    return $?
  else
    return 2
  fi
}

# Helper: normalize a GitHub tag (strip leading v/V, beta- prefixes, trailing metadata)
normalize_version() {
  # examples: v1.2.3 -> 1.2.3    beta3-3.0.0 -> 3.0.0
  printf '%s' "$1" | sed -E 's/^[vV]//; s/^beta[^-]*-//; s/[^0-9.].*$//'
}

# Return 0 if version $1 is less than version $2 (semantic numeric compare)
version_lt() {
  [ "$1" = "$2" ] && return 1
  local i ai bi
  for i in 1 2 3 4 5; do
    ai=$(printf '%s' "$1" | cut -d. -f$i)
    bi=$(printf '%s' "$2" | cut -d. -f$i)
    ai=${ai:-0}
    bi=${bi:-0}
    ai=$(printf '%s' "$ai" | sed 's/[^0-9].*//')
    bi=$(printf '%s' "$bi" | sed 's/[^0-9].*//')
    ai=${ai:-0}
    bi=${bi:-0}
    if [ "$ai" -lt "$bi" ]; then return 0; fi
    if [ "$ai" -gt "$bi" ]; then return 1; fi
  done
  return 1
}

# Try GitHub API: latest → prereleases
REPO="yyyumeniku/HyPrism"
log "Looking for release asset matching: $ASSET_RE"
asset_url=""

# try latest
if command -v curl >/dev/null 2>&1; then
  json=$(curl -sSf "https://api.github.com/repos/$REPO/releases/latest" 2>/dev/null || true)
else
  json=""
fi
if [ -n "$json" ]; then
  asset_url=$(get_asset_url "$json")
  # extract tag_name (e.g. "v2.3.4") and normalize to "2.3.4"
  REMOTE_TAG=$(printf "%s" "$json" | grep -E '"tag_name"' | sed -E 's/.*"tag_name" *: *"([^"]+)".*/\1/' | head -n1 || true)
  REMOTE_VERSION=$(normalize_version "$REMOTE_TAG")
fi

# fallback: search releases for first prerelease with matching asset
if [ -z "$asset_url" ]; then
  if command -v curl >/dev/null 2>&1; then
    json_all=$(curl -sSf "https://api.github.com/repos/$REPO/releases" 2>/dev/null || true)
    if [ -n "$json_all" ]; then
      # prefer non-draft releases; pick first release with matching asset
      asset_url=$(get_asset_url "$json_all")
      if [ -n "$asset_url" ]; then
        # find the tag_name for the release that contains the matched asset
        lineno=$(grep -nF "$asset_url" <<JSON | head -n1 | cut -d: -f1 || true
$json_all
JSON
        )
        if [ -n "$lineno" ]; then
          REMOTE_TAG=$(printf "%s" "$json_all" | head -n "$lineno" | grep -E '"tag_name"' | tail -n1 | sed -E 's/.*"tag_name" *: *"([^"]+)".*/\1/' || true)
          REMOTE_VERSION=$(normalize_version "$REMOTE_TAG")
        fi
      fi
    fi
  fi
fi

# If a user-installed launcher exists, check its version and auto-update if a newer
# release is available on GitHub. If we cannot determine versions, fall back to running
# the installed binary.
if [ -x "$DATA_DIR/HyPrism" ]; then
  INSTALLED_VER=""
  if [ -f "$DATA_DIR/version.txt" ]; then
    INSTALLED_VER=$(sed -n '1p' "$DATA_DIR/version.txt" 2>/dev/null || true)
    INSTALLED_VER=$(normalize_version "$INSTALLED_VER")
  else
    # Try common CLI version flags; these are best-effort and optional.
    if "$DATA_DIR/HyPrism" --version >/dev/null 2>&1; then
      INSTALLED_VER=$("$DATA_DIR/HyPrism" --version 2>/dev/null | head -n1 | sed -E 's/[^0-9.].*$//' || true)
    elif "$DATA_DIR/HyPrism" -v >/dev/null 2>&1; then
      INSTALLED_VER=$("$DATA_DIR/HyPrism" -v 2>/dev/null | head -n1 | sed -E 's/[^0-9.].*$//' || true)
    fi
    if [ -n "$INSTALLED_VER" ]; then
      INSTALLED_VER=$(normalize_version "$INSTALLED_VER")
      printf "%s\n" "$INSTALLED_VER" > "$DATA_DIR/version.txt" 2>/dev/null || true
    fi
  fi

  # If we know both versions, compare and update if needed.
  if [ -n "$REMOTE_VERSION" ] && [ -n "$INSTALLED_VER" ]; then
    if version_lt "$INSTALLED_VER" "$REMOTE_VERSION"; then
      log "Installed launcher version $INSTALLED_VER is older than latest $REMOTE_VERSION — will update"
      # continue to download/extract branch below
    else
      log "Installed launcher is up-to-date ($INSTALLED_VER) — exec"
      exec "$DATA_DIR/HyPrism" "$@"
    fi
  else
    log "Found user release at $DATA_DIR/HyPrism (version unknown) — exec"
    exec "$DATA_DIR/HyPrism" "$@"
  fi
fi

if [ -z "$asset_url" ]; then
  log "No suitable GitHub release asset found; falling back to bundled launcher"
  if [ -x "/app/lib/hyprism/HyPrism" ]; then
    exec /app/lib/hyprism/HyPrism "$@"
  fi
  log "Bundled launcher missing — exiting"
  echo "No launcher available" >&2
  exit 1
fi

log "Downloading asset: $asset_url"
TMP_TAR="$DATA_DIR/hyprism-release.tar.xz"
rm -f "$TMP_TAR"
if ! download_file "$asset_url" "$TMP_TAR"; then
  log "Download failed: $asset_url — falling back to bundled launcher"
  if [ -x "/app/lib/hyprism/HyPrism" ]; then
    exec /app/lib/hyprism/HyPrism "$@"
  fi
  exit 1
fi

log "Extracting release to $DATA_DIR"
# Extract into a temporary dir then move files
TMP_DIR="$DATA_DIR/.extract.$$"
rm -rf "$TMP_DIR" && mkdir -p "$TMP_DIR"
if tar -xJf "$TMP_TAR" -C "$TMP_DIR" 2>>"$LOG"; then
  # find top-level dir containing HyPrism binary
  found_bin=$(find "$TMP_DIR" -type f -name HyPrism -perm /111 | head -n1 || true)
  if [ -n "$found_bin" ]; then
    rm -rf "$DATA_DIR"/* || true
    mkdir -p "$DATA_DIR"
    # copy extracted tree into DATA_DIR preserving structure
    cp -a "$TMP_DIR"/* "$DATA_DIR/" 2>>"$LOG" || true
    chmod +x "$DATA_DIR/HyPrism" 2>>"$LOG" || true
    # save normalized remote version so wrapper can check before next run
    if [ -n "$REMOTE_VERSION" ]; then
      printf "%s\n" "$REMOTE_VERSION" > "$DATA_DIR/version.txt" 2>>"$LOG" || true
    fi
    rm -rf "$TMP_DIR" "$TMP_TAR"
    log "Extraction complete — exec $DATA_DIR/HyPrism"
    exec "$DATA_DIR/HyPrism" "$@"
  else
    log "No HyPrism binary found inside archive — falling back"
    rm -rf "$TMP_DIR" "$TMP_TAR"
    if [ -x "/app/lib/hyprism/HyPrism" ]; then
      exec /app/lib/hyprism/HyPrism "$@"
    fi
    exit 1
  fi
else
  log "Extraction failed" 
  rm -rf "$TMP_DIR" "$TMP_TAR"
  if [ -x "/app/lib/hyprism/HyPrism" ]; then
    exec /app/lib/hyprism/HyPrism "$@"
  fi
  exit 1
fi

# Minimal shim that execs the wrapper shipped in /app/lib/hyprism if present,
# otherwise execs the system wrapper (this file is kept to make bundle builds
# include the wrapper script). The real logic is in Properties/linux/flatpak/hyprism-launcher-wrapper.sh

if [ -x "/app/lib/hyprism/hyprism-launcher-wrapper.sh" ]; then
  exec /app/lib/hyprism/hyprism-launcher-wrapper.sh "$@"
fi

# Fallback to bundled binary
if [ -x "/app/lib/hyprism/HyPrism" ]; then
  exec /app/lib/hyprism/HyPrism "$@"
fi

# Last-resort: fail with message
echo "HyPrism launcher not available inside bundle" >&2
exit 1
