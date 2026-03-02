# SessionStart Hook - Display user messages

$ErrorActionPreference = "SilentlyContinue"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [user-message-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

Write-Log "Hook started"

# Check if this is first run
$workerDll = Join-Path $PluginRoot "src\ClaudeMem.Worker\bin\Release\net9.0\ClaudeMem.Worker.dll"
if (-not (Test-Path $workerDll)) {
    Write-Log "First run - worker not built yet"
    Write-Host "🧠 claude-mem-csharp: First run - building..." -ForegroundColor Yellow
}

# Check worker status
try {
    $health = Invoke-RestMethod -Uri "$WorkerUrl/health" -TimeoutSec 2 -ErrorAction Stop
    $stats = Invoke-RestMethod -Uri "$WorkerUrl/api/stats" -TimeoutSec 2 -ErrorAction SilentlyContinue
    
    Write-Log "Health: OK, Stats: $($stats | ConvertTo-Json -Compress)"
    
    if ($stats -and $stats.observations -gt 0) {
        Write-Host "🧠 claude-mem: $($stats.observations) observations across $($stats.sessions) sessions" -ForegroundColor Cyan
        Write-Host "   View at: $WorkerUrl" -ForegroundColor DarkGray
    }
} catch {
    Write-Log "Error checking worker: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
