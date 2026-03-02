# UserPromptSubmit Hook - Initialize session and save user prompt

$ErrorActionPreference = "SilentlyContinue"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [new-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

# Ensure log directory exists
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

Write-Log "Hook started"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
Write-Log "Input: $inputJson"

$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$cwd = if ($inputData.cwd) { $inputData.cwd } else { "." }
$prompt = if ($inputData.prompt) { $inputData.prompt } else { "" }
$project = Split-Path -Leaf $cwd

Write-Log "SessionId: $sessionId, Project: $project, Prompt length: $($prompt.Length)"

# Skip if no session
if (-not $sessionId) {
    Write-Log "No session_id, skipping"
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Ensure worker is running
Write-Log "Ensuring worker is running..."
& "$PSScriptRoot\ensure-worker.ps1" | Out-Null

# Initialize session via HTTP
try {
    $body = @{
        contentSessionId = $sessionId
        project = $project
        prompt = $prompt
    } | ConvertTo-Json
    
    Write-Log "POST $WorkerUrl/api/sessions/init Body: $body"
    $response = Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/init" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    Write-Log "Response: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Log "Error: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
