# Check Refasmer
$toolName = "Refasmer"
$toolPath = (Get-Command $toolName -ErrorAction SilentlyContinue).Source

if (!$toolPath) {
    Write-Output "Installing $toolName..."
    dotnet tool install -g JetBrains.Refasmer.CliTool
} else {
    Write-Output "$toolName already installed."
}

# Create the destination directory if it does not exist
if (-Not (Test-Path -Path "deps")) {
    New-Item -ItemType Directory -Path "deps"
}

# Use Invoke-WebRequest to download the file
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/Medulla/RefCartActivator.dll" -OutFile "deps/RefCartActivator.dll"
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/Medulla/RefMedullaCore.dll" -OutFile "deps/RefMedullaCore.dll"

