param(
    [ValidateSet("all", "windows", "linux-x64", "linux-arm64")]
    [string]$Target = "all",
    [string]$Configuration = "Release",
    [string]$ZigPath = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $scriptDir "build"
$runtimeDir = Join-Path $buildDir "runtimes"

function Resolve-Tool {
    param(
        [string]$Name,
        [string]$ExplicitPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "$Name was not found at explicit path: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "$Name was not found in PATH."
    }
    return $cmd.Source
}

function New-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Generate-ErrorHeaders {
    $python = Resolve-Tool -Name "python" -ExplicitPath ""
    $generator = Join-Path $scriptDir "c_core\include\msb_error.py"
    & $python $generator c (Join-Path $scriptDir "c_core\include\msb_error_c.h")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & $python $generator cs (Join-Path $scriptDir "wrapper\MCUSerialBridgeError.cs")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Build-WindowsNative {
    Push-Location $scriptDir
    try {
        & scons
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }

    $sourceDll = Join-Path $buildDir "mcu_serial_bridge.dll"
    if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
        throw "Windows native bridge was not produced: $sourceDll"
    }

    $destDir = Join-Path $runtimeDir "win-x64\native"
    New-Directory $destDir
    Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $destDir "mcu_serial_bridge.dll") -Force
}

function Build-LinuxNative {
    param(
        [ValidateSet("linux-x64", "linux-arm64")]
        [string]$Rid
    )

    $zig = Resolve-Tool -Name "zig" -ExplicitPath $ZigPath
    $zigTarget = if ($Rid -eq "linux-arm64") { "aarch64-linux-gnu" } else { "x86_64-linux-gnu" }
    $outputDir = Join-Path $runtimeDir "$Rid\native"
    $outputFile = Join-Path $outputDir "libmcu_serial_bridge.so"
    New-Directory $outputDir

    $sources = @(
        "c_core/src/msb_handle.c",
        "c_core/src/msb_packet.c",
        "c_core/src/msb_thread.c",
        "c_core/src/msb_bridge.c",
        "c_core/src/msb_platform_posix.c",
        "bootloader/src/mbl_bootloader.c"
    ) | ForEach-Object { Join-Path $scriptDir $_ }

    $args = @(
        "cc",
        "-target", $zigTarget,
        "-shared",
        "-fPIC",
        "-std=c11",
        "-D_GNU_SOURCE",
        "-O2",
        "-I", (Join-Path $scriptDir "c_core\include"),
        "-I", (Join-Path $scriptDir "bootloader\include")
    ) + $sources + @(
        "-lpthread",
        "-o", $outputFile
    )

    Write-Host "Building $Rid native bridge with Zig target $zigTarget..."
    & $zig @args
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

New-Directory $buildDir
Generate-ErrorHeaders

if ($Target -eq "all" -or $Target -eq "windows") {
    Build-WindowsNative
}
if ($Target -eq "all" -or $Target -eq "linux-x64") {
    Build-LinuxNative -Rid "linux-x64"
}
if ($Target -eq "all" -or $Target -eq "linux-arm64") {
    Build-LinuxNative -Rid "linux-arm64"
}

Write-Host "Native bridge build completed."
Write-Host "Runtime native assets: $runtimeDir"
