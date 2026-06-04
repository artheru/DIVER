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

$kitDocsSourceDir = Join-Path $repoRoot "3rd\CoralinkerKitDocs"
$kitDocsVersion = if ([string]::IsNullOrWhiteSpace($tag)) { $commit } else { "${tag}-${commit}" }
Write-Host "Publishing Coralinker Kit docs..."
Publish-KitDocs -SourceDir $kitDocsSourceDir -OutputDir $outputDir -Version $kitDocsVersion

$defaultBuildPackages = @(
    [ordered]@{
        include = "Fody"
        version = "6.6.4"
        privateAssets = "all"
        includeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive"
    },
    [ordered]@{
        include = "Newtonsoft.Json"
        version = "13.0.3"
    },
    [ordered]@{
        include = "System.IO.Ports"
        version = "9.0.3"
    },
    [ordered]@{
        include = "System.Management"
        version = "9.0.4"
    }
)

$offlinePackageSpecs = @(
    @{ Id = "fody"; Version = "6.6.4" },
    @{ Id = "newtonsoft.json"; Version = "13.0.3" },
    @{ Id = "system.io.ports"; Version = "9.0.3" },
    @{ Id = "runtime.native.system.io.ports"; Version = "9.0.3" },
    @{ Id = "system.management"; Version = "9.0.4" },
    @{ Id = "system.codedom"; Version = "9.0.4" }
)

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
    startScripts = @("start-host.ps1", "start-host.bat", "start-host.sh")
    setupScripts = @("install-dotnet-sdk-ubuntu.sh", "refresh-package-manifest.sh")
    nativeBridgeRuntimes = @("win-x64", "linux-x64", "linux-arm64")
    nativeSimNodeRuntimes = @("win-x64", "linux-x64", "linux-arm64")
    kitDocs = [ordered]@{
        version = $kitDocsVersion
        markdown = "res/docs/kit/md"
        html = "wwwroot/docs/kit"
    }
    buildPackages = $defaultBuildPackages
    offlineNuGetPackages = $offlinePackageSpecs | ForEach-Object { "$($_.Id)/$($_.Version)" }
    integrityManifest = "package-manifest.sha256"
}

$manifestPath = Join-Path $outputDir "publish-info.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

$packagingDir = Join-Path $scriptDir "packaging"
$packagingFiles = @(
    "start-host.ps1",
    "start-host.bat",
    "start-host.sh",
    "install-dotnet-sdk-ubuntu.sh",
    "refresh-package-manifest.sh"
)
foreach ($packagingFile in $packagingFiles) {
    $packagingSource = Join-Path $packagingDir $packagingFile
    if (-not (Test-Path $packagingSource)) {
        throw "Missing packaging file: $packagingSource"
    }
    Copy-Item -LiteralPath $packagingSource -Destination (Join-Path $outputDir $packagingFile) -Force
}

$offlinePackagesDir = Join-Path $outputDir "res\compiler\nuget-packages"
if (Test-Path -LiteralPath $offlinePackagesDir) {
    Remove-Item -LiteralPath $offlinePackagesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $offlinePackagesDir -Force | Out-Null

$buildPackagesPath = Join-Path $outputDir "res\compiler\build-packages.json"
Write-Utf8NoBom -Path $buildPackagesPath -Content ($defaultBuildPackages | ConvertTo-Json -Depth 5)

$userNuGetPackages = $env:NUGET_PACKAGES
if ([string]::IsNullOrWhiteSpace($userNuGetPackages)) {
    $userNuGetPackages = Join-Path $env:USERPROFILE ".nuget\packages"
}
foreach ($package in $offlinePackageSpecs) {
    $source = Join-Path $userNuGetPackages (Join-Path $package.Id $package.Version)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Missing offline NuGet package '$($package.Id)/$($package.Version)' under '$userNuGetPackages'. Run dotnet restore once on the development machine before publishing."
    }

    $destination = Join-Path $offlinePackagesDir (Join-Path $package.Id $package.Version)
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
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
