param(
    [string]$Configuration = "Release",
    [string]$Runtime = "",
    [switch]$NoRestore,
    [switch]$IncludePdb,
    [switch]$IncludeSdkExecutable,
    [switch]$ExcludeIisConfig,
    [switch]$ExcludeStaticWebAssetsEndpoints,
    [switch]$SkipNativeBuild,
    [string]$ZigPath = ""
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

if (-not $SkipNativeBuild) {
    $nativeBuildScript = Join-Path $repoRoot "MCUSerialBridge\build-native.ps1"
    if (-not (Test-Path -LiteralPath $nativeBuildScript -PathType Leaf)) {
        throw "Missing native build script: $nativeBuildScript"
    }

    $nativeBuildArgs = @("-ExecutionPolicy", "Bypass", "-File", $nativeBuildScript, "-Target", "all", "-Configuration", $Configuration)
    if (-not [string]::IsNullOrWhiteSpace($ZigPath)) {
        $nativeBuildArgs += @("-ZigPath", $ZigPath)
    }

    Write-Host "Building MCUSerialBridge native runtime assets..."
    & powershell -NoProfile @nativeBuildArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

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
Write-Host "Skip native build: $SkipNativeBuild"
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
    skipNativeBuild = [bool]$SkipNativeBuild
    commit = $commit
    commitTime = $commitTime
    dirty = $dirty
    publishTime = $publishTime.ToString("o")
    outputDirectory = $outputDir
    startScripts = @("start-host.ps1", "start-host.bat", "start-host.sh")
    setupScripts = @("install-dotnet-sdk-ubuntu.sh")
    nativeBridgeRuntimes = @("win-x64", "linux-x64", "linux-arm64")
    integrityManifest = "package-manifest.sha256"
}

$manifestPath = Join-Path $outputDir "publish-info.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

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
