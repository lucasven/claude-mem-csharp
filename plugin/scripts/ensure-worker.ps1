# Ensure the claude-mem-csharp Worker is running
# Called by all hooks before making HTTP requests

$ErrorActionPreference = "Stop"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem"
$PidFile = Join-Path $DataDir "worker.pid"
$LogFile = Join-Path $DataDir "worker.log"

# Create data directory
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

function Test-Worker {
    try {
        if (Test-Path $PidFile) {
            $pid = Get-Content $PidFile
            $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($process) {
                $response = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
                return $true
            }
        }
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
    
    # Build if necessary
    $workerDll = Join-Path $PluginRoot "src\ClaudeMem.Worker\bin\Release\net9.0\ClaudeMem.Worker.dll"
    if (-not (Test-Path $workerDll)) {
        Write-Host "Building claude-mem-csharp..." -ForegroundColor Yellow
        Push-Location $PluginRoot
        & dotnet build -c Release --nologo -v q 2>&1 | Out-Null
        Pop-Location
    }
    
    # Start worker in background
    $workerProject = Join-Path $PluginRoot "src\ClaudeMem.Worker"
    $ErrorLog = Join-Path $DataDir "worker-error.log"
    $job = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", $workerProject, "-c", "Release", "--no-build" `
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
    
    Write-Error "Error: Worker failed to start. Check $LogFile"
    exit 1
}

# Main
if (-not (Test-Worker)) {
    Start-Worker
}

Write-Output $WorkerUrl
