#!/usr/bin/env pwsh
# Build script for claude-mem-csharp
# Produces self-contained executables for Windows, Linux, and macOS

param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "all")]
    [string]$Runtime = "all",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "bin"
$SrcDir = Join-Path $RepoRoot "src"

$Runtimes = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")

if ($Runtime -ne "all") {
    $Runtimes = @($Runtime)
}

function Write-Step {
    param([string]$Message)
    Write-Host "`n=> $Message" -ForegroundColor Cyan
}

# Clean output directory
if ($Clean -or !(Test-Path $OutputDir)) {
    Write-Step "Cleaning output directory..."
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Build for each runtime
foreach ($rid in $Runtimes) {
    Write-Step "Building for $rid..."

    $ridOutputDir = Join-Path $OutputDir $rid
    New-Item -ItemType Directory -Path $ridOutputDir -Force | Out-Null

    # Build ClaudeMem.Hooks (CLI)
    Write-Host "  Building ClaudeMem.Hooks..."
    dotnet publish "$SrcDir\ClaudeMem.Hooks\ClaudeMem.Hooks.csproj" `
        -c Release `
        -r $rid `
        -o $ridOutputDir `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error building ClaudeMem.Hooks for $rid" -ForegroundColor Red
        exit 1
    }

    # Build ClaudeMem.Mcp
    Write-Host "  Building ClaudeMem.Mcp..."
    dotnet publish "$SrcDir\ClaudeMem.Mcp\ClaudeMem.Mcp.csproj" `
        -c Release `
        -r $rid `
        -o $ridOutputDir `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error building ClaudeMem.Mcp for $rid" -ForegroundColor Red
        exit 1
    }

    # Build ClaudeMem.Worker
    Write-Host "  Building ClaudeMem.Worker..."
    dotnet publish "$SrcDir\ClaudeMem.Worker\ClaudeMem.Worker.csproj" `
        -c Release `
        -r $rid `
        -o $ridOutputDir `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error building ClaudeMem.Worker for $rid" -ForegroundColor Red
        exit 1
    }

    # Clean up unnecessary files
    Get-ChildItem $ridOutputDir -Filter "*.pdb" | Remove-Item -Force
    Get-ChildItem $ridOutputDir -Filter "*.deps.json" | Remove-Item -Force
    Get-ChildItem $ridOutputDir -Filter "*.runtimeconfig.json" | Remove-Item -Force

    Write-Host "  Build complete for $rid" -ForegroundColor Green
}

Write-Step "Build complete!"
Write-Host "`nOutput directories:"
foreach ($rid in $Runtimes) {
    $ridOutputDir = Join-Path $OutputDir $rid
    Write-Host "  $rid: $ridOutputDir"
}
