#!/usr/bin/env pwsh
# ClaudeMem Installation Script for Windows
# Downloads pre-built self-contained binaries - no .NET SDK required
# Usage: irm https://raw.githubusercontent.com/lucasven/claude-mem-csharp/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$RepoOwner = "lucasven"
$RepoName = "claude-mem-csharp"
$InstallDir = "$env:USERPROFILE\.claude-mem-csharp"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=> $Message" -ForegroundColor Cyan
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

function Get-LatestRelease {
    $apiUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"
    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "claude-mem-csharp-installer" }
        return $release
    }
    catch {
        Write-Host "Error: Failed to get latest release. $_" -ForegroundColor Red
        exit 1
    }
}

# Detect architecture
$Rid = Get-Architecture
Write-Step "Detected platform: $Rid"

# Get latest release
Write-Step "Fetching latest release..."
$release = Get-LatestRelease
$version = $release.tag_name
Write-Host "Latest version: $version"

# Find download URL
$assetName = "claude-mem-csharp-$Rid.zip"
$asset = $release.assets | Where-Object { $_.name -eq $assetName }

if (-not $asset) {
    Write-Host "Error: Could not find release asset: $assetName" -ForegroundColor Red
    Write-Host "Available assets:" -ForegroundColor Yellow
    $release.assets | ForEach-Object { Write-Host "  - $($_.name)" }
    exit 1
}

$downloadUrl = $asset.browser_download_url
Write-Host "Download URL: $downloadUrl"

# Download
Write-Step "Downloading $assetName..."
$tempZip = Join-Path $env:TEMP "claude-mem-csharp-$(Get-Random).zip"

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
}
catch {
    Write-Host "Error: Failed to download release. $_" -ForegroundColor Red
    exit 1
}

# Create installation directory
Write-Step "Installing to $InstallDir..."

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallDir | Out-Null

# Extract
try {
    Expand-Archive -Path $tempZip -DestinationPath $InstallDir -Force

    # Move files from nested directory if present
    $nestedDir = Join-Path $InstallDir $Rid
    if (Test-Path $nestedDir) {
        Get-ChildItem $nestedDir | Move-Item -Destination $InstallDir -Force
        Remove-Item $nestedDir -Force
    }
}
catch {
    Write-Host "Error: Failed to extract archive. $_" -ForegroundColor Red
    exit 1
}
finally {
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
}

# Verify installation
$claudeMemExe = Join-Path $InstallDir "claude-mem-csharp.exe"
if (-not (Test-Path $claudeMemExe)) {
    Write-Host "Error: Installation failed - executable not found" -ForegroundColor Red
    exit 1
}

Write-Step "Installation complete!"

Write-Host ""
Write-Host "ClaudeMem has been installed to: $InstallDir" -ForegroundColor Green
Write-Host "Version: $version"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Option 1: Install as Claude Code plugin (recommended)" -ForegroundColor Cyan
Write-Host "  Run these commands in Claude Code:"
Write-Host "    /plugin marketplace add $RepoOwner/$RepoName"
Write-Host "    /plugin install claude-mem-csharp@claude-mem-csharp-marketplace"
Write-Host ""
Write-Host "  Option 2: Manual configuration" -ForegroundColor Cyan
Write-Host "  1. Start the worker service:"
Write-Host "     $claudeMemExe worker start"
Write-Host ""
Write-Host "  2. Check the status:"
Write-Host "     $claudeMemExe status"
Write-Host ""
Write-Host "  3. Add to your PATH for easier access (optional):"
Write-Host "     `$env:PATH += `";$InstallDir`""
Write-Host ""
