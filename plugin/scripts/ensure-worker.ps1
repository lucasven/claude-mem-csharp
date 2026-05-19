# Ensure the claude-mem-csharp Worker is running
# Called by all hooks before making HTTP requests

$ErrorActionPreference = "Stop"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$PidFile = Join-Path $DataDir "worker.pid"
$LogFile = Join-Path $DataDir "worker.log"
$WorkerDir = Join-Path $DataDir "worker"

# Create data directory
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

function Find-SourceRoot {
    # 1. Explicit env var
    if ($env:CLAUDE_MEM_SOURCE_ROOT -and (Test-Path $env:CLAUDE_MEM_SOURCE_ROOT)) {
        return $env:CLAUDE_MEM_SOURCE_ROOT
    }

    # 2. Derive from cache path: .../plugins/cache/lucasven/... -> .../plugins/marketplaces/lucasven/
    $pluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { "" }
    if ($pluginRoot -match '(.+[\\/]plugins[\\/])cache[\\/]([^\\/]+)') {
        $marketplacePath = Join-Path $Matches[1] "marketplaces" $Matches[2]
        if (Test-Path (Join-Path $marketplacePath "ClaudeMem.sln")) {
            return $marketplacePath
        }
    }

    # 3. Check common marketplace locations
    $claudeDir = Join-Path $env:USERPROFILE ".claude"
    $marketplacesDir = Join-Path $claudeDir "plugins" "marketplaces"
    if (Test-Path $marketplacesDir) {
        $candidates = Get-ChildItem -Path $marketplacesDir -Directory | Where-Object {
            Test-Path (Join-Path $_.FullName "ClaudeMem.sln")
        }
        if ($candidates) {
            return $candidates[0].FullName
        }
    }

    return $null
}

function Test-Worker {
    try {
        if (Test-Path $PidFile) {
            $pid = Get-Content $PidFile -ErrorAction SilentlyContinue
            if ($pid) {
                $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
                if ($process) {
                    $response = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
                    return $true
                }
            }
        }
        # Also try health check without PID (worker may have been started differently)
        $response = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response) { return $true }
    } catch {}
    return $false
}

function Start-Worker {
    # Check if dotnet is available
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Error "Error: .NET SDK not found. Please install .NET 9.0 from https://dotnet.microsoft.com/download/dotnet/9.0"
        exit 1
    }

    # Check for published worker DLL first
    $publishedDll = Join-Path $WorkerDir "ClaudeMem.Worker.dll"

    if (-not (Test-Path $publishedDll)) {
        # Need to build from source
        $sourceRoot = Find-SourceRoot
        if (-not $sourceRoot) {
            Write-Error "Error: Cannot find claude-mem-csharp source. Set CLAUDE_MEM_SOURCE_ROOT env var."
            exit 1
        }

        Write-Host "Building claude-mem-csharp from $sourceRoot ..." -ForegroundColor Yellow

        # Publish to worker directory (framework-dependent, no --self-contained for smaller size)
        & dotnet publish "$sourceRoot\src\ClaudeMem.Worker" -c Release -o $WorkerDir --nologo -v q 2>&1 | Out-Null

        if (-not (Test-Path $publishedDll)) {
            Write-Error "Error: Build failed. Check source at $sourceRoot"
            exit 1
        }

        Write-Host "Build complete." -ForegroundColor Green
    }

    # Start worker in background using the published DLL
    $ErrorLog = Join-Path $DataDir "worker-error.log"
    $job = Start-Process -FilePath "dotnet" -ArgumentList $publishedDll `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $LogFile -RedirectStandardError $ErrorLog

    $job.Id | Out-File -FilePath $PidFile -Encoding ASCII

    # Wait for worker to be ready (max 30 seconds)
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        try {
            $response = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response) { return }
        } catch {}
    }

    Write-Error "Error: Worker failed to start. Check $LogFile and $ErrorLog"
    exit 1
}

# Main
if (-not (Test-Worker)) {
    Start-Worker
}

Write-Output $WorkerUrl
