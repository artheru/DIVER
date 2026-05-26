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

function DotnetInstallHint {
    return @"
Install guidance:
- Ubuntu: run the bundled installer from the package directory:
  sudo ./install-dotnet-sdk-ubuntu.sh
- Other Linux distributions: install .NET SDK 8.0 using your distribution package manager or Microsoft's guide:
  https://learn.microsoft.com/dotnet/core/install/linux
- Windows: install .NET SDK 8.0 from:
  https://dotnet.microsoft.com/download/dotnet/8.0

Version note:
- CoralinkerHost targets net8.0, so Microsoft.NETCore.App 8.x and Microsoft.AspNetCore.App 8.x runtimes are required.
- SDK 9.x is OK for Build if the .NET 8 runtimes are also installed, but SDK 9.x alone cannot replace the .NET 8 runtime.
"@
}

function GitInstallHint {
    return @"
Install guidance:
- Ubuntu/Debian:
  sudo apt-get update
  sudo apt-get install -y git
- Other Linux distributions: install Git using the distribution package manager.
- Windows: install Git for Windows from:
  https://git-scm.com/download/win

Git is required by CoralinkerHost file history, diff, checkout, revert, and project import/export history features.
"@
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
        Fail "dotnet command was not found. Install .NET 8 SDK.`n$(DotnetInstallHint)"
    }

    $sdks = & dotnet --list-sdks 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($sdks | Out-String))) {
        Fail "dotnet SDK is required, but dotnet --list-sdks returned no SDK.`n$(DotnetInstallHint)"
    }

    $hasRequiredSdk = $false
    foreach ($sdk in $sdks) {
        if ($sdk -match '^(\d+)\.' -and [int]$Matches[1] -ge 8) {
            $hasRequiredSdk = $true
            break
        }
    }
    if (-not $hasRequiredSdk) {
        Fail ".NET SDK 8 or newer is required for in-device Build. Installed SDKs: $($sdks -join '; ')`n$(DotnetInstallHint)"
    }

    $runtimes = & dotnet --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($runtimes | Out-String))) {
        Fail ".NET runtimes are required, but dotnet --list-runtimes returned no runtimes.`n$(DotnetInstallHint)"
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
        Fail ".NET 8 runtime and ASP.NET Core 8 runtime are required to run Host. Installed runtimes: $($runtimes -join '; ')`n$(DotnetInstallHint)"
    }
}

function Check-GitEnvironment {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        Fail "git command was not found.`n$(GitInstallHint)"
    }

    $version = & git --version 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($version | Out-String))) {
        Fail "git command exists but failed to run git --version.`n$(GitInstallHint)"
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
Write-Host "Checking Git..."
Check-GitEnvironment
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
