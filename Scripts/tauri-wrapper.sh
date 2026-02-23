#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TAURI_ROOT="$PROJECT_ROOT/TauriWrapper"
TAURI_SRC="$TAURI_ROOT/src-tauri"
SIDECAR_BASENAME="hyprism-bridge"
MODE="${1:-dev}"
TAURI_RUNNER=""
DOTNET_CONFIG="Debug"

log() { echo "[tauri-wrapper] $*"; }
fail() { echo "[tauri-wrapper] ERROR: $*" >&2; exit 1; }

ensure_rust_toolchain() {
  if command -v cargo >/dev/null 2>&1; then
    return
  fi

  # rustup default install path
  if [[ -f "$HOME/.cargo/env" ]]; then
    # shellcheck disable=SC1090
    source "$HOME/.cargo/env"
  fi

  if [[ -x "$HOME/.cargo/bin/cargo" ]]; then
    export PATH="$HOME/.cargo/bin:$PATH"
  fi

  command -v cargo >/dev/null 2>&1 || fail "cargo is required (not found in PATH)"
}

detect_tauri_runner() {
  if cargo tauri --help >/dev/null 2>&1; then
    TAURI_RUNNER="cargo-tauri-subcommand"
    return
  fi

  if command -v cargo-tauri >/dev/null 2>&1; then
    TAURI_RUNNER="cargo-tauri-binary"
    return
  fi

  if [[ -x "$TAURI_ROOT/node_modules/.bin/tauri" ]]; then
    TAURI_RUNNER="npm-tauri-cli"
    return
  fi

  fail "Tauri CLI is required (install with: cargo install tauri-cli --version '^2' OR npm i in TauriWrapper/)"
}

check_linux_tauri_deps() {
  [[ "$(uname -s)" == "Linux" ]] || return 0

  command -v pkg-config >/dev/null 2>&1 || fail "pkg-config is required for Linux Tauri builds"

  local missing=()
  local pkg
  for pkg in glib-2.0 gobject-2.0 gio-2.0 gdk-3.0; do
    if ! pkg-config --exists "$pkg"; then
      missing+=("$pkg")
    fi
  done

  if [[ ${#missing[@]} -gt 0 ]]; then
    echo "[tauri-wrapper] Missing Linux system libraries: ${missing[*]}" >&2
    echo "[tauri-wrapper] Install GTK/WebKit dev deps first." >&2
    echo "[tauri-wrapper] Ubuntu/Debian example:" >&2
    echo "  sudo apt-get install -y libgtk-3-dev libwebkit2gtk-4.1-dev libayatana-appindicator3-dev" >&2
    fail "Linux Tauri prerequisites are not installed"
  fi
}

run_tauri() {
  local cmd="$1"

  case "$TAURI_RUNNER" in
    cargo-tauri-subcommand)
      (cd "$TAURI_SRC" && cargo tauri "$cmd")
      ;;
    cargo-tauri-binary)
      (cd "$TAURI_SRC" && cargo-tauri "$cmd")
      ;;
    npm-tauri-cli)
      (cd "$TAURI_SRC" && npm exec tauri "$cmd")
      ;;
    *)
      fail "internal error: unknown TAURI_RUNNER='$TAURI_RUNNER'"
      ;;
  esac
}

command -v dotnet >/dev/null 2>&1 || fail "dotnet is required"
ensure_rust_toolchain
detect_tauri_runner
check_linux_tauri_deps

RID=""
case "$(uname -s)" in
  Linux*)
    case "$(uname -m)" in
      x86_64) RID="linux-x64" ;;
      aarch64|arm64) RID="linux-arm64" ;;
      *) fail "unsupported Linux architecture: $(uname -m)" ;;
    esac
    ;;
  Darwin*)
    case "$(uname -m)" in
      arm64) RID="osx-arm64" ;;
      x86_64) RID="osx-x64" ;;
      *) fail "unsupported macOS architecture: $(uname -m)" ;;
    esac
    ;;
  MINGW*|MSYS*|CYGWIN*)
    RID="win-x64"
    ;;
  *)
    fail "unsupported host OS: $(uname -s)"
    ;;
esac

TARGET_TRIPLE=""
case "$RID" in
  linux-x64) TARGET_TRIPLE="x86_64-unknown-linux-gnu" ;;
  linux-arm64) TARGET_TRIPLE="aarch64-unknown-linux-gnu" ;;
  osx-arm64) TARGET_TRIPLE="aarch64-apple-darwin" ;;
  osx-x64) TARGET_TRIPLE="x86_64-apple-darwin" ;;
  win-x64) TARGET_TRIPLE="x86_64-pc-windows-msvc" ;;
  *) fail "unsupported RID mapping: $RID" ;;
esac

SIDE_EXT=""
if [[ "$RID" == win-* ]]; then
  SIDE_EXT=".exe"
fi

STAGING_DIR="$PROJECT_ROOT/.tauri-sidecar/$RID"
DEST_DIR="$TAURI_SRC/bin"
DEST_FILE="$DEST_DIR/${SIDECAR_BASENAME}-${TARGET_TRIPLE}${SIDE_EXT}"

prepare_frontend() {
  log "Building frontend assets into wwwroot/"
  dotnet build "$PROJECT_ROOT/HyPrism.csproj" -v minimal >/dev/null
}

prepare_sidecar() {
  mkdir -p "$STAGING_DIR" "$DEST_DIR"

  log "Building .NET bridge sidecar (RID: $RID, config: $DOTNET_CONFIG)"
  dotnet build "$PROJECT_ROOT/HyPrism.csproj" \
    -c "$DOTNET_CONFIG" \
    -r "$RID" \
    -v minimal >/dev/null

  local source_bin="$PROJECT_ROOT/bin/$DOTNET_CONFIG/net10.0/$RID/HyPrism$SIDE_EXT"
  [[ -f "$source_bin" ]] || fail "bridge binary not found: $source_bin"

  if [[ "$RID" == win-* ]]; then
    cp "$source_bin" "$DEST_FILE"
    chmod +x "$DEST_FILE" || true
  else
    cat > "$DEST_FILE" <<EOF
#!/usr/bin/env bash
set -euo pipefail
exec "$source_bin" "\$@"
EOF
    chmod +x "$DEST_FILE"
  fi

  log "Prepared Tauri sidecar launcher: $DEST_FILE"
}

run_dev() {
  log "Running Tauri wrapper in dev mode"
  run_tauri dev
}

run_build() {
  log "Building Tauri wrapper bundle"
  run_tauri build
}

case "$MODE" in
  dev)
    DOTNET_CONFIG="Debug"
    prepare_frontend
    prepare_sidecar
    run_dev
    ;;
  build)
    DOTNET_CONFIG="Release"
    prepare_frontend
    prepare_sidecar
    run_build
    ;;
  *)
    fail "unknown mode: $MODE (expected: dev|build)"
    ;;
esac
