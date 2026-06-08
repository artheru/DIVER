#!/usr/bin/env sh
set -eu

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

info() {
  echo "[install-dotnet-sdk-ubuntu] $*"
}

[ "$(id -u 2>/dev/null || echo "")" = "0" ] || fail "This installer must run as root. Use: sudo bash setup/install-dotnet-sdk-ubuntu.sh"

command -v apt-get >/dev/null 2>&1 || fail "apt-get was not found. This installer supports Ubuntu/Debian-style systems only."

if [ ! -r /etc/os-release ]; then
  fail "/etc/os-release was not found; cannot detect Ubuntu version."
fi

# shellcheck disable=SC1091
. /etc/os-release

[ "${ID:-}" = "ubuntu" ] || fail "This installer supports Ubuntu only. Detected ID=${ID:-unknown}."
[ -n "${VERSION_ID:-}" ] || fail "Ubuntu VERSION_ID was not found in /etc/os-release."

ARCH="$(dpkg --print-architecture 2>/dev/null || true)"
case "$ARCH" in
  amd64|arm64)
    ;;
  *)
    fail "Unsupported architecture: ${ARCH:-unknown}. Expected amd64 or arm64."
    ;;
esac

if command -v dotnet >/dev/null 2>&1; then
  SDK_LIST="$(dotnet --list-sdks 2>/dev/null || true)"
  if printf '%s\n' "$SDK_LIST" | grep -Eq '^([8-9]|[1-9][0-9]+)\.'; then
    info ".NET SDK 8 or newer is already installed:"
    printf '%s\n' "$SDK_LIST"
    exit 0
  fi
fi

REPO_DEB_URL="https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb"
TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT INT TERM

info "Detected Ubuntu ${VERSION_ID} (${ARCH})."
info "Installing prerequisites..."
apt-get update
apt-get install -y ca-certificates wget apt-transport-https gpg git

info "Downloading Microsoft package feed: ${REPO_DEB_URL}"
wget -O "$TMP_DIR/packages-microsoft-prod.deb" "$REPO_DEB_URL"

info "Registering Microsoft package feed..."
dpkg -i "$TMP_DIR/packages-microsoft-prod.deb"

info "Installing dotnet-sdk-8.0..."
apt-get update
apt-get install -y dotnet-sdk-8.0

info "Installed SDKs:"
dotnet --list-sdks

info "Installed runtimes:"
dotnet --list-runtimes

info "Installed Git:"
git --version

info "Done."
