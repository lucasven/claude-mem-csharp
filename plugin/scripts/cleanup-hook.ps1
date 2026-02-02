# SessionEnd Hook - Mark session as completed

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$reason = if ($inputData.reason) { $inputData.reason } else { "exit" }

# Skip if no session
if (-not $sessionId) {
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Fire-and-forget HTTP POST
Start-Job -ScriptBlock {
    param($url, $body)
    try {
        Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -TimeoutSec 2 | Out-Null
    } catch {}
} -ArgumentList "$WorkerUrl/api/sessions/complete", (@{
    contentSessionId = $sessionId
    reason = $reason
} | ConvertTo-Json) | Out-Null

'{"continue": true, "suppressOutput": true}'
