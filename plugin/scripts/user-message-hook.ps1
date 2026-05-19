# SessionStart Hook - Display memory stats

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"
$WorkerDir = Join-Path $DataDir "worker"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [user-message-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

Write-Log "Hook started"

# Check if worker binaries exist
$workerDll = Join-Path $WorkerDir "ClaudeMem.Worker.dll"
if (-not (Test-Path $workerDll)) {
    Write-Log "First run - worker not published yet"
}

# Check worker status
try {
    $health = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction Stop
    $stats = Invoke-RestMethod -Uri "$WorkerUrl/api/stats" -TimeoutSec 2 -ErrorAction SilentlyContinue

    Write-Log "Health: OK, Stats: $($stats | ConvertTo-Json -Compress)"

    if ($stats -and $stats.database -and $stats.database.observations -gt 0) {
        Write-Host "claude-mem: $($stats.database.observations) observations across $($stats.database.sessions) sessions" -ForegroundColor Cyan
    }
} catch {
    Write-Log "Error checking worker: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
