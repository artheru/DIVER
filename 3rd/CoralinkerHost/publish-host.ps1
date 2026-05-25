param(
    [string]$Configuration = "Release",
    [string]$Runtime = "",
    [switch]$NoRestore
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

Write-Host "Publishing CoralinkerHost..."
Write-Host "Commit: $commit ($commitTime)"
Write-Host "Publish time: $($publishTime.ToString("o"))"
Write-Host "Output: $outputDir"

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$manifest = [ordered]@{
    app = "CoralinkerHost"
    configuration = $Configuration
    runtime = if ([string]::IsNullOrWhiteSpace($Runtime)) { $null } else { $Runtime }
    commit = $commit
    commitTime = $commitTime
    dirty = $dirty
    publishTime = $publishTime.ToString("o")
    outputDirectory = $outputDir
}

$manifestPath = Join-Path $outputDir "publish-info.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Publish completed."
Write-Host "Manifest: $manifestPath"
