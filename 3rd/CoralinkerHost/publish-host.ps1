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
$tag = (& git -C $repoRoot describe --tags --always --dirty 2>$null).Trim()
if ([string]::IsNullOrWhiteSpace($tag)) {
    $tag = $null
}
$dirty = -not [string]::IsNullOrWhiteSpace((& git -C $repoRoot status --porcelain))
$publishTime = Get-Date
$publishStamp = $publishTime.ToString("yyyyMMdd-HHmmss")
$publishName = "CoralinkerHost_${commit}_${publishStamp}"
$publishRoot = Join-Path $scriptDir "Publish"
$outputDir = Join-Path $publishRoot $publishName
$appDir = Join-Path $outputDir "app"
$setupDir = Join-Path $outputDir "setup"
$metaDir = Join-Path $outputDir "meta"
$packagingDir = Join-Path $scriptDir "packaging"

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

function Copy-PackageScript {
    param(
        [string]$Source,
        [string]$Destination
    )

    $extension = [System.IO.Path]::GetExtension($Source)
    if ($extension -ieq ".sh") {
        $content = Get-Content -LiteralPath $Source -Raw -Encoding UTF8
        Write-LfUtf8NoBom -Path $Destination -Content $content
        return
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Convert-InlineMarkdownToHtml {
    param([string]$Text)
    $encoded = [System.Net.WebUtility]::HtmlEncode($Text)
    $encoded = [regex]::Replace($encoded, '`([^`]+)`', '<code>$1</code>')
    $encoded = [regex]::Replace($encoded, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    $encoded = [regex]::Replace($encoded, '\[([^\]]+)\]\(([^)]+)\)', {
        param($m)
        $label = $m.Groups[1].Value
        $href = $m.Groups[2].Value
        if ($href -match '\.md($|[#?])') {
            $href = $href -replace '\.md($|[#?])', '.html$1'
        }
        "<a href=`"$href`">$label</a>"
    })
    return $encoded
}

function Convert-MarkdownToKitHtml {
    param(
        [string]$MarkdownPath,
        [string]$Markdown,
        [string]$Version,
        [array]$NavItems
    )

    $title = [System.IO.Path]::GetFileNameWithoutExtension($MarkdownPath)
    foreach ($line in ($Markdown -replace "`r`n", "`n").Split("`n")) {
        if ($line.StartsWith("# ")) {
            $title = $line.Substring(2).Trim()
            break
        }
    }

    $body = New-Object System.Text.StringBuilder
    $paragraph = New-Object System.Text.StringBuilder
    $inCode = $false
    $inList = $false
    $inTable = $false

    function Flush-Paragraph {
        if ($paragraph.Length -gt 0) {
            [void]$body.Append("<p>")
            [void]$body.Append((Convert-InlineMarkdownToHtml $paragraph.ToString().Trim()))
            [void]$body.AppendLine("</p>")
            [void]$paragraph.Clear()
        }
    }
    function Close-List {
        if ((Get-Variable -Name inList -Scope 1).Value) {
            [void]$body.AppendLine("</ul>")
            Set-Variable -Name inList -Scope 1 -Value $false
        }
    }
    function Close-Table {
        if ((Get-Variable -Name inTable -Scope 1).Value) {
            [void]$body.AppendLine("</tbody></table>")
            Set-Variable -Name inTable -Scope 1 -Value $false
        }
    }

    foreach ($rawLine in ($Markdown -replace "`r`n", "`n").Split("`n")) {
        $line = $rawLine.TrimEnd("`r")
        if ($line.StartsWith('```')) {
            Flush-Paragraph
            Close-List
            Close-Table
            if ($inCode) {
                [void]$body.AppendLine("</code></pre>")
                $inCode = $false
            } else {
                [void]$body.Append("<pre><code>")
                $inCode = $true
            }
            continue
        }
        if ($inCode) {
            [void]$body.Append([System.Net.WebUtility]::HtmlEncode($line))
            [void]$body.Append("`n")
            continue
        }
        if ([string]::IsNullOrWhiteSpace($line)) {
            Flush-Paragraph
            Close-List
            Close-Table
            continue
        }
        if ($line.StartsWith("|") -and $line.EndsWith("|")) {
            Flush-Paragraph
            Close-List
            if ($line.Contains("---")) { continue }
            if (-not $inTable) {
                [void]$body.AppendLine("<table><tbody>")
                $inTable = $true
            }
            [void]$body.Append("<tr>")
            foreach ($cell in $line.Trim("|").Split("|")) {
                [void]$body.Append("<td>")
                [void]$body.Append((Convert-InlineMarkdownToHtml $cell.Trim()))
                [void]$body.Append("</td>")
            }
            [void]$body.AppendLine("</tr>")
            continue
        }
        Close-Table
        if ($line -match '^(#{1,6})\s+(.+)$') {
            Flush-Paragraph
            Close-List
            $level = $Matches[1].Length
            [void]$body.Append("<h$level>")
            [void]$body.Append((Convert-InlineMarkdownToHtml $Matches[2].Trim()))
            [void]$body.AppendLine("</h$level>")
            continue
        }
        if ($line.StartsWith("- ")) {
            Flush-Paragraph
            if (-not $inList) {
                [void]$body.AppendLine("<ul>")
                $inList = $true
            }
            [void]$body.Append("<li>")
            [void]$body.Append((Convert-InlineMarkdownToHtml $line.Substring(2).Trim()))
            [void]$body.AppendLine("</li>")
            continue
        }
        if ($paragraph.Length -gt 0) {
            [void]$paragraph.Append(" ")
        }
        [void]$paragraph.Append($line.Trim())
    }
    Flush-Paragraph
    Close-List
    Close-Table
    if ($inCode) {
        [void]$body.AppendLine("</code></pre>")
    }

    $nav = New-Object System.Text.StringBuilder
    foreach ($item in $NavItems) {
        $label = [System.Net.WebUtility]::HtmlEncode([System.IO.Path]::GetFileNameWithoutExtension($item))
        $href = [System.Net.WebUtility]::HtmlEncode(($item -replace '\.md$', '.html'))
        [void]$nav.AppendLine("<a href=`"$href?v=$Version`">$label</a>")
    }

    $safeTitle = [System.Net.WebUtility]::HtmlEncode($title)
    return @"
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>$safeTitle - Coralinker Kit Docs</title>
  <style>
    :root { color-scheme: light dark; font-family: "Segoe UI", Arial, sans-serif; }
    body { margin: 0; line-height: 1.65; }
    header { padding: 16px 24px; border-bottom: 1px solid #8884; }
    main { display: grid; grid-template-columns: minmax(220px, 280px) minmax(0, 1fr); gap: 24px; max-width: 1280px; margin: 0 auto; padding: 24px; }
    nav { position: sticky; top: 16px; align-self: start; font-size: 14px; }
    nav a { display: block; padding: 4px 0; text-decoration: none; }
    article { max-width: 920px; }
    code { padding: 0.1em 0.25em; border-radius: 4px; background: #8882; }
    pre { overflow: auto; padding: 14px; border-radius: 8px; background: #8882; }
    pre code { padding: 0; background: transparent; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid #8885; padding: 6px 8px; vertical-align: top; }
    .version { color: #777; font-size: 13px; }
    @media (max-width: 800px) { main { display: block; } nav { position: static; margin-bottom: 24px; } }
  </style>
</head>
<body>
  <header><strong>Coralinker Kit Docs</strong> <span class="version">version=$Version</span></header>
  <main>
    <nav>$($nav.ToString())</nav>
    <article>$($body.ToString())</article>
  </main>
</body>
</html>
"@
}

function Publish-KitDocs {
    param(
        [string]$SourceDir,
        [string]$OutputDir,
        [string]$Version
    )

    if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
        throw "Missing Kit docs source: $SourceDir"
    }

    $mdOut = Join-Path $OutputDir "res\docs\kit\md"
    $htmlOut = Join-Path $OutputDir "wwwroot\docs\kit"
    if (Test-Path -LiteralPath $mdOut) { Remove-Item -LiteralPath $mdOut -Recurse -Force }
    if (Test-Path -LiteralPath $htmlOut) { Remove-Item -LiteralPath $htmlOut -Recurse -Force }
    New-Item -ItemType Directory -Path $mdOut -Force | Out-Null
    New-Item -ItemType Directory -Path $htmlOut -Force | Out-Null

    $sourceRoot = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $files = Get-ChildItem -LiteralPath $SourceDir -Recurse -File |
        Where-Object { $_.FullName.Replace('\', '/') -notmatch '/_write_request/' }

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length)
        $mdDest = Join-Path $mdOut $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $mdDest) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $mdDest -Force
    }

    $navItems = Get-ChildItem -LiteralPath $SourceDir -Filter "*.md" -File |
        Sort-Object Name |
        ForEach-Object { $_.Name }

    foreach ($file in ($files | Where-Object { $_.Extension -ieq ".md" })) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        $htmlRelative = [System.IO.Path]::ChangeExtension($relative, ".html").Replace('\', '/')
        $htmlDest = Join-Path $htmlOut $htmlRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $htmlDest) -Force | Out-Null
        $markdown = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        $html = Convert-MarkdownToKitHtml -MarkdownPath $relative -Markdown $markdown -Version $Version -NavItems $navItems
        Write-Utf8NoBom -Path $htmlDest -Content $html
    }

    foreach ($file in ($files | Where-Object { $_.Extension -ine ".md" })) {
        $relative = $file.FullName.Substring($sourceRoot.Length)
        $htmlDest = Join-Path $htmlOut $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $htmlDest) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $htmlDest -Force
    }

    $resourceFiles = @()
    foreach ($file in ($files | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        $kind = "resource"
        if ($relative -like "tools/*") { $kind = "tool" }
        elseif ($relative -like "examples/*") { $kind = "example" }
        elseif ($relative -like "stubs/*") { $kind = "stub" }
        elseif ($relative -like "runtime/*") { $kind = "runtime" }
        elseif ($file.Extension -ieq ".md") { $kind = "doc" }
        $resourceFiles += [ordered]@{
            path = $relative
            kind = $kind
            markdownUrl = "/api/docs/kit/md/$relative`?v=$([uri]::EscapeDataString($Version))"
            htmlUrl = if ($file.Extension -ieq ".md") { "/docs/kit/$([System.IO.Path]::ChangeExtension($relative, ".html").Replace('\', '/'))`?v=$([uri]::EscapeDataString($Version))" } else { "/docs/kit/$relative`?v=$([uri]::EscapeDataString($Version))" }
            sizeBytes = $file.Length
            lastModifiedUtc = $file.LastWriteTimeUtc.ToString("O")
        }
    }

    $resources = [ordered]@{
        version = $Version
        bundleUrl = "/api/docs/kit/bundle.zip?v=$([uri]::EscapeDataString($Version))"
        entry = "README.md"
        recommendedReadOrder = @("README.md", "00-system-overview.md", "01-quickstart.md", "09-agent-workflows.md", "10-agent-api.md", "11-multinode-system-design-reference.md", "runtime/fact-template.md", "tools/README.md", "02-logic-api.md", "04-variables-and-io.md", "06-remote-control.md")
        files = $resourceFiles
    }
    $resourcesJson = $resources | ConvertTo-Json -Depth 8
    Write-Utf8NoBom -Path (Join-Path $mdOut "resources.json") -Content $resourcesJson
    Write-Utf8NoBom -Path (Join-Path $htmlOut "resources.json") -Content $resourcesJson

    $bundlePath = Join-Path $htmlOut "bundle.zip"
    if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
    Compress-Archive -Path (Join-Path $mdOut "*") -DestinationPath $bundlePath -Force
}

function Write-PackageReadme {
    param(
        [string]$Path,
        [string]$PackageName
    )

    $content = @"
# CoralinkerHost Portable Package

Package: $PackageName

## Start

Windows PowerShell:

    .\start-host.ps1

Windows CMD:

    start-host.bat

Ubuntu/Linux:

    sudo bash ./start-host.sh

If .NET SDK 8 is missing on Ubuntu, run:

    sudo bash setup/install-dotnet-sdk-ubuntu.sh

## Layout

- app/: Host binaries, wwwroot/, compiler resources, runtime assets, and runtime data.
- setup/: installation and maintenance scripts.
- meta/: package integrity manifest.
- publish-info.json: version and publish metadata.

Run checks without starting Host:

    sudo bash ./start-host.sh --check-only
"@
    Write-LfUtf8NoBom -Path $Path -Content $content
}

function ConvertTo-PackageReferenceXml {
    param([array]$Packages)

    $packageList = @($Packages)
    if ($packageList.Count -eq 1 -and $packageList[0] -is [System.Array]) {
        $packageList = @($packageList[0])
    }

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($package in $packageList) {
        if ([string]::IsNullOrWhiteSpace($package.include) -or [string]::IsNullOrWhiteSpace($package.version)) {
            throw "Invalid build package entry. Each item must include 'include' and 'version'."
        }

        $include = [System.Security.SecurityElement]::Escape([string]$package.include)
        $version = [System.Security.SecurityElement]::Escape([string]$package.version)
        $privateAssets = [System.Security.SecurityElement]::Escape([string]$package.privateAssets)
        $includeAssets = [System.Security.SecurityElement]::Escape([string]$package.includeAssets)

        if ([string]::IsNullOrWhiteSpace($privateAssets) -and [string]::IsNullOrWhiteSpace($includeAssets)) {
            $lines.Add("    <PackageReference Include=`"$include`" Version=`"$version`" />")
            continue
        }

        $lines.Add("    <PackageReference Include=`"$include`" Version=`"$version`">")
        if (-not [string]::IsNullOrWhiteSpace($privateAssets)) {
            $lines.Add("      <PrivateAssets>$privateAssets</PrivateAssets>")
        }
        if (-not [string]::IsNullOrWhiteSpace($includeAssets)) {
            $lines.Add("      <IncludeAssets>$includeAssets</IncludeAssets>")
        }
        $lines.Add("    </PackageReference>")
    }

    return ($lines -join "`n")
}

function Get-BuildPackageClosure {
    param(
        [array]$BuildPackages,
        [string]$ProbeDir
    )

    if (Test-Path -LiteralPath $ProbeDir) {
        Remove-Item -LiteralPath $ProbeDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ProbeDir -Force | Out-Null

    $packageReferencesXml = ConvertTo-PackageReferenceXml -Packages $BuildPackages
    $probeProjectPath = Join-Path $ProbeDir "CoralinkerBuildPackageProbe.csproj"
    $probeProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
$packageReferencesXml
  </ItemGroup>
</Project>
"@
    Write-Utf8NoBom -Path $probeProjectPath -Content $probeProject

    Write-Host "Restoring build package probe to calculate offline NuGet package closure..."
    & dotnet restore $probeProjectPath --verbosity minimal 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Build package probe restore failed. Restore the Host project once on the development machine, or ensure NuGet sources are reachable, then publish again."
    }

    $assetsPath = Join-Path $ProbeDir "obj\project.assets.json"
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "Build package probe restore did not produce project.assets.json: $assetsPath"
    }

    $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageFolders = @()
    if ($assets.packageFolders) {
        $packageFolders += $assets.packageFolders.PSObject.Properties.Name
    }
    if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $packageFolders += $env:NUGET_PACKAGES
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $packageFolders += (Join-Path $env:USERPROFILE ".nuget\packages")
    }
    $packageFolders = $packageFolders |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path -LiteralPath $_ -ErrorAction SilentlyContinue).Path } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    if ($packageFolders.Count -eq 0) {
        throw "No NuGet package folders were reported by project.assets.json."
    }

    $packages = @{}
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne "package") {
            continue
        }

        $libraryKey = [string]$library.Name
        $slashIndex = $libraryKey.IndexOf("/")
        if ($slashIndex -le 0 -or $slashIndex -ge ($libraryKey.Length - 1)) {
            throw "Unexpected package library key in project.assets.json: $($library.Name)"
        }

        $id = $libraryKey.Substring(0, $slashIndex).ToLowerInvariant()
        $version = $libraryKey.Substring($slashIndex + 1)
        $relativePath = if ($library.Value.path) { [string]$library.Value.path } else { "$id/$version" }
        $sourcePath = $null
        foreach ($folder in $packageFolders) {
            $candidate = Join-Path $folder $relativePath
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $sourcePath = $candidate
                break
            }
        }

        if ($null -eq $sourcePath) {
            throw "Restore resolved package '$id/$version', but the package folder '$relativePath' was not found under: $($packageFolders -join '; ')"
        }

        $key = "$id/$version"
        $packages[$key] = [pscustomobject]@{
            Id = $id
            Version = $version
            RelativePath = $relativePath
            SourcePath = $sourcePath
        }
    }

    return $packages.Values | Sort-Object Id, Version
}

if (-not $SkipNativeBuild) {
    $nativeBuildScripts = @(
        @{ Name = "MCUSerialBridge"; Path = Join-Path $repoRoot "MCUSerialBridge\build-native.ps1" },
        @{ Name = "CoralinkerSimNodeHost"; Path = Join-Path $repoRoot "3rd\CoralinkerSimNodeHost\build-native.ps1" }
    )

    foreach ($nativeBuild in $nativeBuildScripts) {
        if (-not (Test-Path -LiteralPath $nativeBuild.Path -PathType Leaf)) {
            throw "Missing native build script: $($nativeBuild.Path)"
        }

        $nativeBuildArgs = @("-ExecutionPolicy", "Bypass", "-File", $nativeBuild.Path, "-Target", "all", "-Configuration", $Configuration)
        if (-not [string]::IsNullOrWhiteSpace($ZigPath)) {
            $nativeBuildArgs += @("-ZigPath", $ZigPath)
        }

        Write-Host "Building $($nativeBuild.Name) native runtime assets..."
        & powershell -NoProfile @nativeBuildArgs
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

$publishArgs = @("publish", $projectPath, "-c", $Configuration, "-o", $appDir)
if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
    $publishArgs += @("-r", $Runtime, "--self-contained", "false")
}
if ($NoRestore) {
    $publishArgs += "--no-restore"
}
if (-not $IncludePdb) {
    $publishArgs += @("-p:DebugType=None", "-p:DebugSymbols=false")
}

$clientAppSourceDir = Join-Path $scriptDir "ClientApp"
if (-not (Test-Path -LiteralPath (Join-Path $clientAppSourceDir "package.json") -PathType Leaf)) {
    throw "Missing ClientApp package.json: $clientAppSourceDir"
}

Write-Host "Building CoralinkerHost frontend..."
Push-Location $clientAppSourceDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

$packageJson = Get-Content -LiteralPath (Join-Path $clientAppSourceDir "package.json") -Raw | ConvertFrom-Json
$frontendBuildInfo = [ordered]@{
    app = "CoralinkerHost UI"
    version = $packageJson.version
    tag = $tag
    commit = $commit
    commitTime = $commitTime
    buildTime = $publishTime.ToString("o")
    configuration = $Configuration
    dirty = $dirty
}
Write-Utf8NoBom -Path (Join-Path $scriptDir "wwwroot\build-info.json") -Content ($frontendBuildInfo | ConvertTo-Json)

Write-Host "Publishing CoralinkerHost..."
Write-Host "Commit: $commit ($commitTime)"
Write-Host "Tag: $tag"
Write-Host "Publish time: $($publishTime.ToString("o"))"
Write-Host "Include PDB: $IncludePdb"
Write-Host "Include SDK executable: $IncludeSdkExecutable"
Write-Host "Exclude IIS config: $ExcludeIisConfig"
Write-Host "Exclude static web assets endpoints: $ExcludeStaticWebAssetsEndpoints"
Write-Host "Skip native build: $SkipNativeBuild"
Write-Host "Output: $outputDir"
Write-Host "Application directory: $appDir"

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$clientAppDir = Join-Path $appDir "ClientApp"
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
        $sidecarPath = Join-Path $appDir $sidecar
        if (Test-Path $sidecarPath) {
            Remove-Item -Force $sidecarPath
        }
    }
}

if ($ExcludeIisConfig) {
    $iisConfigPath = Join-Path $appDir "web.config"
    if (Test-Path $iisConfigPath) {
        Remove-Item -Force $iisConfigPath
    }
}

if ($ExcludeStaticWebAssetsEndpoints) {
    $staticWebAssetsEndpointsPath = Join-Path $appDir "CoralinkerHost.staticwebassets.endpoints.json"
    if (Test-Path $staticWebAssetsEndpointsPath) {
        Remove-Item -Force $staticWebAssetsEndpointsPath
    }
}

if (-not $IncludePdb) {
    Get-ChildItem -Path $appDir -Recurse -Filter "*.pdb" -File | Remove-Item -Force
}

$kitDocsSourceDir = Join-Path $repoRoot "3rd\CoralinkerKitDocs"
$kitDocsVersion = if ([string]::IsNullOrWhiteSpace($tag)) { $commit } else { "${tag}-${commit}" }
Write-Host "Publishing Coralinker Kit docs..."
Publish-KitDocs -SourceDir $kitDocsSourceDir -OutputDir $appDir -Version $kitDocsVersion

$buildPackagesSourcePath = Join-Path $packagingDir "build-packages.json"
if (-not (Test-Path -LiteralPath $buildPackagesSourcePath -PathType Leaf)) {
    throw "Missing build package configuration: $buildPackagesSourcePath"
}
$buildPackagesJson = Get-Content -LiteralPath $buildPackagesSourcePath -Raw -Encoding UTF8
$defaultBuildPackages = @($buildPackagesJson | ConvertFrom-Json)
if ($defaultBuildPackages.Count -eq 1 -and $defaultBuildPackages[0] -is [System.Array]) {
    $defaultBuildPackages = @($defaultBuildPackages[0])
}
$packageProbeDir = Join-Path ([System.IO.Path]::GetTempPath()) "coralinker-build-package-probe-$publishStamp"
$offlinePackageSpecs = Get-BuildPackageClosure -BuildPackages $defaultBuildPackages -ProbeDir $packageProbeDir
try {
    if (Test-Path -LiteralPath $packageProbeDir) {
        Remove-Item -LiteralPath $packageProbeDir -Recurse -Force
    }
} catch {
    Write-Warning "Could not remove temporary package probe directory: $packageProbeDir"
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
    tag = $tag
    commit = $commit
    commitTime = $commitTime
    dirty = $dirty
    publishTime = $publishTime.ToString("o")
    outputDirectory = $outputDir
    appDirectory = "app"
    setupDirectory = "setup"
    metaDirectory = "meta"
    startScripts = @("start-host.ps1", "start-host.bat", "start-host.sh")
    setupScripts = @("setup/install-dotnet-sdk-ubuntu.sh", "setup/refresh-package-manifest.sh")
    nativeBridgeRuntimes = @("win-x64", "linux-x64", "linux-arm64")
    nativeSimNodeRuntimes = @("win-x64", "linux-x64", "linux-arm64")
    kitDocs = [ordered]@{
        version = $kitDocsVersion
        markdown = "app/res/docs/kit/md"
        html = "app/wwwroot/docs/kit"
    }
    buildPackages = @($defaultBuildPackages)
    offlineNuGetPackages = $offlinePackageSpecs | ForEach-Object { "$($_.Id)/$($_.Version)" }
    integrityManifest = "meta/package-manifest.sha256"
}

$manifestPath = Join-Path $outputDir "publish-info.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

$rootScripts = @("start-host.ps1", "start-host.bat", "start-host.sh")
foreach ($packagingFile in $rootScripts) {
    $packagingSource = Join-Path $packagingDir $packagingFile
    if (-not (Test-Path $packagingSource)) {
        throw "Missing packaging file: $packagingSource"
    }
    Copy-PackageScript -Source $packagingSource -Destination (Join-Path $outputDir $packagingFile)
}

New-Item -ItemType Directory -Path $setupDir -Force | Out-Null
$setupScripts = @("install-dotnet-sdk-ubuntu.sh", "refresh-package-manifest.sh")
foreach ($packagingFile in $setupScripts) {
    $packagingSource = Join-Path $packagingDir $packagingFile
    if (-not (Test-Path $packagingSource)) {
        throw "Missing packaging file: $packagingSource"
    }
    Copy-PackageScript -Source $packagingSource -Destination (Join-Path $setupDir $packagingFile)
}

Write-PackageReadme -Path (Join-Path $outputDir "README.md") -PackageName $publishName

$offlinePackagesDir = Join-Path $appDir "res\compiler\nuget-packages"
if (Test-Path -LiteralPath $offlinePackagesDir) {
    Remove-Item -LiteralPath $offlinePackagesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $offlinePackagesDir -Force | Out-Null

$buildPackagesPath = Join-Path $appDir "res\compiler\build-packages.json"
Write-Utf8NoBom -Path $buildPackagesPath -Content (@($defaultBuildPackages) | ConvertTo-Json -Depth 5)

foreach ($package in $offlinePackageSpecs) {
    $source = $package.SourcePath
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Missing offline NuGet package '$($package.Id)/$($package.Version)' at '$source'. Run dotnet restore once on the development machine before publishing."
    }

    $destination = Join-Path $offlinePackagesDir $package.RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

New-Item -ItemType Directory -Path $metaDir -Force | Out-Null
$outputRootForManifest = (Resolve-Path $outputDir).Path.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$integrityLines = Get-ChildItem -Path $outputDir -Recurse -File |
    Where-Object { $_.FullName -ne (Join-Path $metaDir "package-manifest.sha256") } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($outputRootForManifest.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relative"
    }
Write-LfUtf8NoBom -Path (Join-Path $metaDir "package-manifest.sha256") -Content (($integrityLines -join "`n") + "`n")

Write-Host "Publish completed."
Write-Host "Manifest: $manifestPath"
