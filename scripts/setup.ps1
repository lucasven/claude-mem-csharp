#!/usr/bin/env pwsh
# Plugin setup script - downloads binaries for the current platform
# Called automatically when plugin is installed via Claude Code

param(
    [string]$PluginRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$RepoOwner = "lucasven"
$RepoName = "claude-mem-csharp"
$BinDir = Join-Path $PluginRoot "bin"

function Write-Step {
    param([string]$Message)
    Write-Host "=> $Message" -ForegroundColor Cyan
}

function Get-Architecture {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    switch ($arch) {
        "X64" { return "win-x64" }
        "Arm64" { return "win-arm64" }
        default {
            Write-Host "Unsupported architecture: $arch" -ForegroundColor Red
            exit 1
        }
    }
}

# Check if binaries already exist
if ((Test-Path (Join-Path $BinDir "claude-mem-csharp.exe")) -and
    (Test-Path (Join-Path $BinDir "ClaudeMem.Mcp.exe")) -and
    (Test-Path (Join-Path $BinDir "ClaudeMem.Worker.exe"))) {
    Write-Host "Binaries already installed."
    exit 0
}

$Rid = Get-Architecture
Write-Step "Detected platform: $Rid"

# Get latest release
Write-Step "Fetching latest release..."
$apiUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"
$release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "claude-mem-csharp-setup" }
$version = $release.tag_name

# Find download URL
$assetName = "claude-mem-csharp-$Rid.zip"
$asset = $release.assets | Where-Object { $_.name -eq $assetName }

if (-not $asset) {
    Write-Host "Error: Could not find release asset: $assetName" -ForegroundColor Red
    exit 1
}

# Download
Write-Step "Downloading $assetName..."
$tempZip = Join-Path $env:TEMP "claude-mem-csharp-setup-$(Get-Random).zip"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip -UseBasicParsing

# Extract to bin directory
Write-Step "Installing to $BinDir..."
New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $BinDir -Force

# Move files from nested directory if present
$nestedDir = Join-Path $BinDir $Rid
if (Test-Path $nestedDir) {
    Get-ChildItem $nestedDir | Move-Item -Destination $BinDir -Force
    Remove-Item $nestedDir -Force
}

Remove-Item $tempZip -Force

Write-Step "Setup complete! Version: $version"
