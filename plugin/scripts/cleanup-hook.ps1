# SessionEnd Hook - Mark session as completed

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [cleanup-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

Write-Log "Hook started"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
Write-Log "Input: $inputJson"

$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$reason = if ($inputData.reason) { $inputData.reason } else { "exit" }

Write-Log "SessionId: $sessionId, Reason: $reason"

# Skip if no session
if (-not $sessionId) {
    Write-Log "No session_id, skipping"
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# HTTP POST
try {
    $body = @{
        contentSessionId = $sessionId
        reason = $reason
    } | ConvertTo-Json
    
    Write-Log "POST $WorkerUrl/api/sessions/complete"
    $response = Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/complete" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    Write-Log "Response: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Log "Error: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
