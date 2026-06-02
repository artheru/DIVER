#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR"
CHECK_ONLY=0
SKIP_INTEGRITY_CHECK=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    --check-only)
      CHECK_ONLY=1
      shift
      ;;
    --skip-integrity-check)
      SKIP_INTEGRITY_CHECK=1
      shift
      ;;
    --)
      shift
      break
      ;;
    *)
      break
      ;;
  esac
done

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

dotnet_install_hint() {
  cat >&2 <<'EOF'
Install guidance:
- Ubuntu: run the bundled installer from the package directory:
  sudo ./install-dotnet-sdk-ubuntu.sh
- Other Linux distributions: install .NET SDK 8.0 using your distribution package manager or Microsoft's guide:
  https://learn.microsoft.com/dotnet/core/install/linux

Version note:
- CoralinkerHost targets net8.0, so Microsoft.NETCore.App 8.x and Microsoft.AspNetCore.App 8.x runtimes are required.
- SDK 9.x is OK for Build if the .NET 8 runtimes are also installed, but SDK 9.x alone cannot replace the .NET 8 runtime.
EOF
}

git_install_hint() {
  cat >&2 <<'EOF'
Install guidance:
- Ubuntu/Debian:
  sudo apt-get update
  sudo apt-get install -y git
- Other Linux distributions: install Git using the distribution package manager.

Git is required by CoralinkerHost file history, diff, checkout, revert, and project import/export history features.
EOF
}

fail_dotnet() {
  echo "ERROR: $*" >&2
  dotnet_install_hint
  exit 1
}

fail_git() {
  echo "ERROR: $*" >&2
  git_install_hint
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "Missing required file: $1"
}

require_dir() {
  [ -d "$1" ] || fail "Missing required directory: $1"
}

CURRENT_UID=$(id -u 2>/dev/null || echo "")
[ "$CURRENT_UID" = "0" ] || fail "Linux startup must run as root. Re-run with sudo or root user."

command -v dotnet >/dev/null 2>&1 || fail_dotnet "dotnet command was not found. Install .NET 8 SDK."
SDK_LIST=$(dotnet --list-sdks 2>/dev/null || true)
[ -n "$SDK_LIST" ] || fail_dotnet "dotnet SDK is required, but dotnet --list-sdks returned no SDK."
printf '%s\n' "$SDK_LIST" | grep -Eq '^([8-9]|[1-9][0-9]+)\.' || fail_dotnet ".NET SDK 8 or newer is required for in-device Build. Installed SDKs: $SDK_LIST"
RUNTIME_LIST=$(dotnet --list-runtimes 2>/dev/null || true)
[ -n "$RUNTIME_LIST" ] || fail_dotnet ".NET runtimes are required, but dotnet --list-runtimes returned no runtimes."
printf '%s\n' "$RUNTIME_LIST" | grep -q '^Microsoft\.NETCore\.App 8\.' || fail_dotnet ".NET 8 runtime is required to run Host. Installed runtimes: $RUNTIME_LIST"
printf '%s\n' "$RUNTIME_LIST" | grep -q '^Microsoft\.AspNetCore\.App 8\.' || fail_dotnet "ASP.NET Core 8 runtime is required to run Host. Installed runtimes: $RUNTIME_LIST"

command -v git >/dev/null 2>&1 || fail_git "git command was not found."
git --version >/dev/null 2>&1 || fail_git "git command exists but failed to run git --version."

require_file "CoralinkerHost.dll"
require_file "CoralinkerHost.deps.json"
require_file "CoralinkerHost.runtimeconfig.json"
require_file "publish-info.json"
require_dir "wwwroot"
require_dir "res"
require_dir "res/compiler"
require_file "res/compiler/DiverCompiler.dll"
require_file "res/compiler/DiverCompiler.deps.json"
require_file "res/compiler/RunOnMCU.cs"
require_file "res/compiler/DIVERInterface.cs"
require_file "res/compiler/DIVERCommonUtils.cs"
require_file "res/compiler/Extensions.cs"
require_file "res/compiler/build-packages.json"
require_dir "res/compiler/nuget-packages"
require_file "runtimes/win-x64/native/mcu_serial_bridge.dll"
require_file "runtimes/linux-x64/native/libmcu_serial_bridge.so"
require_file "runtimes/linux-arm64/native/libmcu_serial_bridge.so"

if [ "$SKIP_INTEGRITY_CHECK" = "1" ]; then
  echo "WARNING: package integrity check skipped by --skip-integrity-check."
else
  require_file "package-manifest.sha256"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c package-manifest.sha256
  else
    fail "sha256sum command was not found; cannot verify package integrity."
  fi
fi

if [ "$CHECK_ONLY" = "1" ]; then
  echo "Startup checks passed."
  exit 0
fi

echo "Starting CoralinkerHost..."
exec dotnet "$SCRIPT_DIR/CoralinkerHost.dll" "$@"
