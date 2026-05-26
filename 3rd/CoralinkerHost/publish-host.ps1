param(
    [string]$Configuration = "Release",
    [string]$Runtime = "",
    [switch]$NoRestore,
    [switch]$IncludePdb,
    [switch]$IncludeSdkExecutable,
    [switch]$ExcludeIisConfig,
    [switch]$ExcludeStaticWebAssetsEndpoints
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$projectPath = Join-Path $scriptDir "CoralinkerHost.csproj"

$commit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
$commitTime = (& git -C $repoRoot show -s --format=%cI HEAD).Trim()
$dirty = -not [string]::IsNullOrWhiteSpace((& git -C $repoRoot status --porcelain))
$publishTime = Get-Date
$publishStamp = $publishTime.ToString("yyyyMMdd-HHmmss")
$publishName = "CoralinkerHost_${commit}_${publishStamp}"
$publishRoot = Join-Path $scriptDir "Publish"
$outputDir = Join-Path $publishRoot $publishName

$publishArgs = @("publish", $projectPath, "-c", $Configuration, "-o", $outputDir)
if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
    $publishArgs += @("-r", $Runtime, "--self-contained", "false")
}
if ($NoRestore) {
    $publishArgs += "--no-restore"
}
if (-not $IncludePdb) {
    $publishArgs += @("-p:DebugType=None", "-p:DebugSymbols=false")
}

Write-Host "Publishing CoralinkerHost..."
Write-Host "Commit: $commit ($commitTime)"
Write-Host "Publish time: $($publishTime.ToString("o"))"
Write-Host "Include PDB: $IncludePdb"
Write-Host "Include SDK executable: $IncludeSdkExecutable"
Write-Host "Exclude IIS config: $ExcludeIisConfig"
Write-Host "Exclude static web assets endpoints: $ExcludeStaticWebAssetsEndpoints"
Write-Host "Output: $outputDir"

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$clientAppDir = Join-Path $outputDir "ClientApp"
if (Test-Path $clientAppDir) {
    Remove-Item -Recurse -Force $clientAppDir
}

if (-not $IncludeSdkExecutable) {
    # CoralinkerSDK is referenced by Host as a library; its standalone apphost
    # sidecars are not needed in the default Host distribution package.
    $sdkExecutableSidecars = @(
        "CoralinkerSDK.exe",
        "CoralinkerSDK",
        "CoralinkerSDK.deps.json",
        "CoralinkerSDK.runtimeconfig.json"
    )
    foreach ($sidecar in $sdkExecutableSidecars) {
        $sidecarPath = Join-Path $outputDir $sidecar
        if (Test-Path $sidecarPath) {
            Remove-Item -Force $sidecarPath
        }
    }
}

if ($ExcludeIisConfig) {
    $iisConfigPath = Join-Path $outputDir "web.config"
    if (Test-Path $iisConfigPath) {
        Remove-Item -Force $iisConfigPath
    }
}

if ($ExcludeStaticWebAssetsEndpoints) {
    $staticWebAssetsEndpointsPath = Join-Path $outputDir "CoralinkerHost.staticwebassets.endpoints.json"
    if (Test-Path $staticWebAssetsEndpointsPath) {
        Remove-Item -Force $staticWebAssetsEndpointsPath
    }
}

if (-not $IncludePdb) {
    Get-ChildItem -Path $outputDir -Recurse -Filter "*.pdb" -File | Remove-Item -Force
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Write-LfUtf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )
    Write-Utf8NoBom -Path $Path -Content ($Content -replace "`r`n", "`n")
}

$manifest = [ordered]@{
    app = "CoralinkerHost"
    configuration = $Configuration
    runtime = if ([string]::IsNullOrWhiteSpace($Runtime)) { $null } else { $Runtime }
    includePdb = [bool]$IncludePdb
    includeSdkExecutable = [bool]$IncludeSdkExecutable
    excludeIisConfig = [bool]$ExcludeIisConfig
    excludeStaticWebAssetsEndpoints = [bool]$ExcludeStaticWebAssetsEndpoints
    commit = $commit
    commitTime = $commitTime
    dirty = $dirty
    publishTime = $publishTime.ToString("o")
    outputDirectory = $outputDir
    startScripts = @("start-host.ps1", "start-host.bat", "start-host.sh")
    setupScripts = @("install-dotnet-sdk-ubuntu.sh")
    integrityManifest = "package-manifest.sha256"
}

$manifestPath = Join-Path $outputDir "publish-info.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

$startHostPs1 = @'
param(
    [switch]$CheckOnly,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$HostArgs
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Require-File([string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $ScriptDir $RelativePath) -PathType Leaf)) {
        Fail "Missing required file: $RelativePath"
    }
}

function Require-Directory([string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $ScriptDir $RelativePath) -PathType Container)) {
        Fail "Missing required directory: $RelativePath"
    }
}

function Check-DotnetEnvironment {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        Fail "dotnet command was not found. Install .NET 8 SDK."
    }

    $sdks = & dotnet --list-sdks 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($sdks | Out-String))) {
        Fail "dotnet SDK is required, but dotnet --list-sdks returned no SDK."
    }

    $hasRequiredSdk = $false
    foreach ($sdk in $sdks) {
        if ($sdk -match '^(\d+)\.' -and [int]$Matches[1] -ge 8) {
            $hasRequiredSdk = $true
            break
        }
    }
    if (-not $hasRequiredSdk) {
        Fail ".NET SDK 8 or newer is required for in-device Build. Installed SDKs: $($sdks -join '; ')"
    }

    $runtimes = & dotnet --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($runtimes | Out-String))) {
        Fail ".NET runtimes are required, but dotnet --list-runtimes returned no runtimes."
    }

    $hasNetCore8 = $false
    $hasAspNetCore8 = $false
    foreach ($runtime in $runtimes) {
        if ($runtime -match '^Microsoft\.NETCore\.App\s+8\.') {
            $hasNetCore8 = $true
        }
        if ($runtime -match '^Microsoft\.AspNetCore\.App\s+8\.') {
            $hasAspNetCore8 = $true
        }
    }
    if (-not $hasNetCore8 -or -not $hasAspNetCore8) {
        Fail ".NET 8 runtime and ASP.NET Core 8 runtime are required to run Host. Installed runtimes: $($runtimes -join '; ')"
    }
}

function Check-PackageFiles {
    Require-File "CoralinkerHost.dll"
    Require-File "CoralinkerHost.deps.json"
    Require-File "CoralinkerHost.runtimeconfig.json"
    Require-File "publish-info.json"
    Require-Directory "wwwroot"
    Require-Directory "res"
    Require-Directory "res/compiler"
    Require-File "res/compiler/DiverCompiler.dll"
    Require-File "res/compiler/DiverCompiler.deps.json"
    Require-File "res/compiler/RunOnMCU.cs"
    Require-File "res/compiler/DIVERInterface.cs"
    Require-File "res/compiler/DIVERCommonUtils.cs"
    Require-File "res/compiler/Extensions.cs"
    Require-File "package-manifest.sha256"
}

function Check-PackageIntegrity {
    $manifestPath = Join-Path $ScriptDir "package-manifest.sha256"
    $lines = Get-Content -LiteralPath $manifestPath
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line.Length -lt 67) {
            Fail "Invalid integrity manifest line: $line"
        }

        $expected = $line.Substring(0, 64).ToLowerInvariant()
        $relative = $line.Substring(66).TrimStart('*')
        $relativePath = $relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $fullPath = Join-Path $ScriptDir $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            Fail "Integrity check failed; missing file: $relative"
        }

        $actual = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Fail "Integrity check failed; hash mismatch: $relative"
        }
    }
}

Write-Host "Checking .NET runtime and SDK..."
Check-DotnetEnvironment
Write-Host "Checking required package files..."
Check-PackageFiles
Write-Host "Checking package integrity..."
Check-PackageIntegrity
if ($CheckOnly) {
    Write-Host "Startup checks passed."
    exit 0
}
Write-Host "Starting CoralinkerHost..."

& dotnet (Join-Path $ScriptDir "CoralinkerHost.dll") @HostArgs
exit $LASTEXITCODE
'@

$startHostBat = @'
@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%start-host.ps1" %*
exit /b %ERRORLEVEL%
'@

$startHostSh = @'
#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR"
CHECK_ONLY=0
if [ "${1:-}" = "--check-only" ]; then
  CHECK_ONLY=1
  shift
fi

fail() {
  echo "ERROR: $*" >&2
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

command -v dotnet >/dev/null 2>&1 || fail "dotnet command was not found. Install .NET 8 SDK."
SDK_LIST=$(dotnet --list-sdks 2>/dev/null || true)
[ -n "$SDK_LIST" ] || fail "dotnet SDK is required, but dotnet --list-sdks returned no SDK."
printf '%s\n' "$SDK_LIST" | grep -Eq '^([8-9]|[1-9][0-9]+)\.' || fail ".NET SDK 8 or newer is required for in-device Build. Installed SDKs: $SDK_LIST"
RUNTIME_LIST=$(dotnet --list-runtimes 2>/dev/null || true)
[ -n "$RUNTIME_LIST" ] || fail ".NET runtimes are required, but dotnet --list-runtimes returned no runtimes."
printf '%s\n' "$RUNTIME_LIST" | grep -q '^Microsoft\.NETCore\.App 8\.' || fail ".NET 8 runtime is required to run Host. Installed runtimes: $RUNTIME_LIST"
printf '%s\n' "$RUNTIME_LIST" | grep -q '^Microsoft\.AspNetCore\.App 8\.' || fail "ASP.NET Core 8 runtime is required to run Host. Installed runtimes: $RUNTIME_LIST"

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
require_file "package-manifest.sha256"

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum -c package-manifest.sha256
else
  fail "sha256sum command was not found; cannot verify package integrity."
fi

if [ "$CHECK_ONLY" = "1" ]; then
  echo "Startup checks passed."
  exit 0
fi

echo "Starting CoralinkerHost..."
exec dotnet "$SCRIPT_DIR/CoralinkerHost.dll" "$@"
'@

Write-Utf8NoBom -Path (Join-Path $outputDir "start-host.ps1") -Content $startHostPs1
Write-Utf8NoBom -Path (Join-Path $outputDir "start-host.bat") -Content $startHostBat
Write-LfUtf8NoBom -Path (Join-Path $outputDir "start-host.sh") -Content $startHostSh

$packagingDir = Join-Path $scriptDir "packaging"
$packagingFiles = @(
    "start-host.ps1",
    "start-host.bat",
    "start-host.sh",
    "install-dotnet-sdk-ubuntu.sh"
)
foreach ($packagingFile in $packagingFiles) {
    $packagingSource = Join-Path $packagingDir $packagingFile
    if (-not (Test-Path $packagingSource)) {
        throw "Missing packaging file: $packagingSource"
    }
    Copy-Item -LiteralPath $packagingSource -Destination (Join-Path $outputDir $packagingFile) -Force
}

$outputRootForManifest = (Resolve-Path $outputDir).Path.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$integrityLines = Get-ChildItem -Path $outputDir -Recurse -File |
    Where-Object { $_.Name -ne "package-manifest.sha256" } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($outputRootForManifest.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relative"
    }
Write-LfUtf8NoBom -Path (Join-Path $outputDir "package-manifest.sha256") -Content (($integrityLines -join "`n") + "`n")

Write-Host "Publish completed."
Write-Host "Manifest: $manifestPath"
