# UserPromptSubmit Hook - Initialize session and save user prompt

$ErrorActionPreference = "SilentlyContinue"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$cwd = if ($inputData.cwd) { $inputData.cwd } else { "." }
$prompt = if ($inputData.prompt) { $inputData.prompt } else { "" }
$project = Split-Path -Leaf $cwd

# Skip if no session
if (-not $sessionId) {
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Ensure worker is running
& "$PSScriptRoot\ensure-worker.ps1" | Out-Null

# Initialize session via HTTP
try {
    $body = @{
        contentSessionId = $sessionId
        project = $project
        prompt = $prompt
    } | ConvertTo-Json
    
    Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/init" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5 | Out-Null
} catch {}

'{"continue": true, "suppressOutput": true}'
