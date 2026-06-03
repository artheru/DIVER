param(
    [ValidateSet("all", "windows", "linux-x64", "linux-arm64")]
    [string]$Target = "all",
    [string]$Configuration = "Release",
    [string]$ZigPath = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$mcuRuntimeDir = Join-Path $repoRoot "MCURuntime"
$runtimeDir = Join-Path $scriptDir "build\runtimes"
$runtimeSource = Join-Path $mcuRuntimeDir "mcu_runtime.c"
$shimSource = Join-Path $scriptDir "native\sim_node_runtime.c"

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

function Resolve-VsDevCmd {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw "vswhere.exe was not found: $vswhere"
    }

    $installPath = (& $vswhere -latest -products * -property installationPath).Trim()
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw "Visual Studio installation was not found."
    }

    $vcvars = Join-Path $installPath "VC\Auxiliary\Build\vcvars64.bat"
    if (-not (Test-Path -LiteralPath $vcvars -PathType Leaf)) {
        throw "vcvars64.bat was not found: $vcvars"
    }

    return $vcvars
}

function Build-WindowsNative {
    $outputDir = Join-Path $runtimeDir "win-x64\native"
    $outputFile = Join-Path $outputDir "sim_node_runtime.dll"
    New-Directory $outputDir

    $vcvars = Resolve-VsDevCmd
    $debugFlag = if ($Configuration -eq "Debug") { "/MDd /Od" } else { "/MD /O2" }
    $cmd = "call `"$vcvars`" && cl /W0 /LD $debugFlag /DSIM_NODE_HOST /I`"$mcuRuntimeDir`" /Zi /EHsc `"$runtimeSource`" `"$shimSource`" /Fe:`"$outputFile`" /link /DEBUG"

    Write-Host "Building win-x64 sim_node_runtime with MSVC..."
    & cmd /c $cmd
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
        throw "Windows sim_node_runtime was not produced: $outputFile"
    }
}

function Build-LinuxNative {
    param(
        [ValidateSet("linux-x64", "linux-arm64")]
        [string]$Rid
    )

    $zig = Resolve-Tool -Name "zig" -ExplicitPath $ZigPath
    $zigTarget = if ($Rid -eq "linux-arm64") { "aarch64-linux-gnu" } else { "x86_64-linux-gnu" }
    $outputDir = Join-Path $runtimeDir "$Rid\native"
    $outputFile = Join-Path $outputDir "libsim_node_runtime.so"
    New-Directory $outputDir

    $args = @(
        "cc",
        "-target", $zigTarget,
        "-shared",
        "-fPIC",
        "-std=gnu11",
        "-DSIM_NODE_HOST",
        "-O2",
        "-I", $mcuRuntimeDir,
        $runtimeSource,
        $shimSource,
        "-lm",
        "-o", $outputFile
    )

    Write-Host "Building $Rid sim_node_runtime with Zig target $zigTarget..."
    & $zig @args
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($Target -eq "all" -or $Target -eq "windows") {
    Build-WindowsNative
}
if ($Target -eq "all" -or $Target -eq "linux-x64") {
    Build-LinuxNative -Rid "linux-x64"
}
if ($Target -eq "all" -or $Target -eq "linux-arm64") {
    Build-LinuxNative -Rid "linux-arm64"
}

Write-Host "SimNode native build completed."
Write-Host "Runtime native assets: $runtimeDir"
