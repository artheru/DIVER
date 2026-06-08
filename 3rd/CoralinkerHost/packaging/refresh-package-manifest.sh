#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PACKAGE_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
META_DIR="$PACKAGE_ROOT/meta"
cd "$PACKAGE_ROOT"

if ! command -v sha256sum >/dev/null 2>&1; then
  echo "ERROR: sha256sum command was not found." >&2
  exit 1
fi

mkdir -p "$META_DIR"
tmp_manifest="meta/package-manifest.sha256.tmp"
find . -type f ! -path './meta/package-manifest.sha256' ! -path "./$tmp_manifest" -print \
  | sed 's#^\./##' \
  | sort \
  | while IFS= read -r file; do
      hash=$(sha256sum "$file" | awk '{print $1}')
      printf '%s *%s\n' "$hash" "$file"
    done > "$tmp_manifest"

mv "$tmp_manifest" "meta/package-manifest.sha256"
echo "meta/package-manifest.sha256 refreshed."
