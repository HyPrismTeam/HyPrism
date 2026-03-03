#!/usr/bin/env bash
set -euo pipefail

# update-flathub-manifest.sh
# - copy all regular files (not directories) from Properties/linux/flatpak -> Properties/linux/flathub
# - download (or read locally) flathub-bundle.tar.gz, compute sha256 and update
#   `Properties/linux/flathub/io.github.hyprismteam.HyPrism.yml` by replacing the placeholder

usage() {
  cat <<EOF
Usage: $0 [-s SRC_DIR] [-d DST_DIR] [-a ARCHIVE_URL_OR_PATH] [-r README_SRC]

Options:
  -s SRC_DIR    source folder with flatpak files (default: Properties/linux/flatpak)
  -d DST_DIR    destination folder for flathub files (default: Properties/linux/flathub)
  -a ARCHIVE    URL or local path to flathub-bundle.tar.gz (optional — if given the
                script will download/inspect it and insert the sha256 into the manifest)
  -r README_SRC optional path to a README file which will replace the README.md in the
                destination folder (useful to replace with the repo README)
  -h            show this help

Examples:
  # copy files only
  $0

  # copy files + download archive and patch manifest sha256
  $0 -a https://github.com/OWNER/REPO/releases/download/vX.Y/flathub-bundle.tar.gz

  # copy files and replace README.md in the flathub folder
  $0 -r README.md
EOF
  exit 1
}

SRC_DIR="Properties/linux/flatpak"
DST_DIR="Properties/linux/flathub"
ARCHIVE=""
README_SRC=""

while getopts ":s:d:a:r:h" opt; do
  case $opt in
    s) SRC_DIR="$OPTARG" ;;
    d) DST_DIR="$OPTARG" ;;
    a) ARCHIVE="$OPTARG" ;;
    r) README_SRC="$OPTARG" ;;
    h) usage ;;
    \?) echo "Invalid option: -$OPTARG" >&2; usage ;;
  esac
done

mkdir -p "$DST_DIR"

# 1) copy regular files (no directories)
echo "Copying regular files from '$SRC_DIR' -> '$DST_DIR'..."
if [ ! -d "$SRC_DIR" ]; then
  echo "Source directory '$SRC_DIR' does not exist." >&2
  exit 2
fi
find "$SRC_DIR" -maxdepth 1 -type f -exec cp -a -- '{}' "$DST_DIR/" \;

# optional: replace README in destination with provided source
if [ -n "${README_SRC:-}" ]; then
  if [ -f "$README_SRC" ]; then
    echo "Replacing README in '$DST_DIR' with '$README_SRC'"
    cp -a "$README_SRC" "$DST_DIR/README.md"
  else
    echo "README source not found: $README_SRC" >&2
    exit 5
  fi
fi

MANIFEST="$DST_DIR/io.github.hyprismteam.HyPrism.yml"
# if no manifest in DST, try to copy from SRC
if [ ! -f "$MANIFEST" ]; then
  if [ -f "$SRC_DIR/io.github.hyprismteam.HyPrism.yml" ]; then
    echo "Creating manifest '$MANIFEST' by copying from $SRC_DIR"
    cp -a "$SRC_DIR/io.github.hyprismteam.HyPrism.yml" "$MANIFEST"
  else
    echo "Manifest not found in source or destination: $MANIFEST" >&2
    exit 3
  fi
fi

# helper: insert/replace sha256 in manifest
insert_sha_in_manifest() {
  local sha="$1"
  # 3 possible states in manifest:
  #  1) placeholder `REPLACE_WITH_FLATHUB_BUNDLE_SHA256` — replace it
  #  2) already `type: archive` — update sha field
  #  3) still has the two `type: dir` blocks — replace those with archive + sha

  if grep -q "REPLACE_WITH_FLATHUB_BUNDLE_SHA256" "$MANIFEST"; then
    sed -i "s/REPLACE_WITH_FLATHUB_BUNDLE_SHA256/$sha/" "$MANIFEST"
    return
  fi

  if grep -q "type: archive" "$MANIFEST"; then
    # replace existing sha value
    sed -i -E "s/(sha256:\s*)[a-f0-9]{64}/\1$sha/" "$MANIFEST"
    return
  fi

  # replace the two `type: dir` blocks with the archive block
  perl -0777 -pe "s{(\n\s*sources:\n)\s*- type: dir\n\s*path: ../../../bin/Release/net10.0/linux-x64/publish/linux-unpacked\n\s*dest: linux-unpacked\n\s*- type: dir\n\s*path: ../../../wwwroot\n\s*dest: wwwroot\n}{\1      - type: archive\n        url: release, latest, flathub-bundle.tar.gz\n        sha256: $sha\n}s" -i "$MANIFEST"
}

# 2) if archive provided -> download (or use local path), compute sha and patch manifest
if [ -n "$ARCHIVE" ]; then
  TMPDIR="$(mktemp -d)"
  trap 'rm -rf "$TMPDIR"' EXIT

  if [[ "$ARCHIVE" =~ ^https?:// ]]; then
    TARPATH="$TMPDIR/flathub-bundle.tar.gz"
    echo "Downloading archive from $ARCHIVE..."
    curl -fL -o "$TARPATH" "$ARCHIVE"
  else
    TARPATH="$ARCHIVE"
    if [ ! -f "$TARPATH" ]; then
      echo "Archive file not found: $TARPATH" >&2
      exit 4
    fi
  fi

  echo "Computing sha256..."
  if command -v sha256sum >/dev/null 2>&1; then
    SHA256="$(sha256sum "$TARPATH" | awk '{print $1}')"
  else
    SHA256="$(shasum -a 256 "$TARPATH" | awk '{print $1}')"
  fi
  echo "sha256=$SHA256"

  # quick verification that the tar contains both expected directories
  if tar -tzf "$TARPATH" | awk -F/ '{print $1}' | sort -u | grep -E "^(wwwroot|linux-unpacked)$" >/dev/null; then
    echo "Archive contains expected entries (wwwroot, linux-unpacked)"
  else
    echo "Warning: archive does NOT appear to contain both 'wwwroot' and 'linux-unpacked' (continuing)" >&2
  fi

  insert_sha_in_manifest "$SHA256"
  echo "Patched manifest: $MANIFEST"
else
  echo "No archive provided (-a); manifest left with placeholder or will contain archive block after manual update."
fi

echo "Done. Files copied to '$DST_DIR' and manifest updated (if -a was used)."
exit 0
