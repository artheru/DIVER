#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR"

if ! command -v sha256sum >/dev/null 2>&1; then
  echo "ERROR: sha256sum command was not found." >&2
  exit 1
fi

tmp_manifest="package-manifest.sha256.tmp"
find . -type f ! -name 'package-manifest.sha256' ! -name "$tmp_manifest" -print \
  | sed 's#^\./##' \
  | sort \
  | while IFS= read -r file; do
      hash=$(sha256sum "$file" | awk '{print $1}')
      printf '%s *%s\n' "$hash" "$file"
    done > "$tmp_manifest"

mv "$tmp_manifest" package-manifest.sha256
echo "package-manifest.sha256 refreshed."
