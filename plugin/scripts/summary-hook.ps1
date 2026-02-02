# Stop Hook - Generate session summary

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$transcriptPath = $inputData.transcript_path

# Skip if no session
if (-not $sessionId) {
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Extract last messages from transcript
$lastUserMsg = ""
$lastAssistantMsg = ""

if ($transcriptPath -and (Test-Path $transcriptPath)) {
    try {
        $content = Get-Content $transcriptPath -Raw
        $userMatches = [regex]::Matches($content, '"role":"user".*?"content":"([^"]*)"')
        $assistantMatches = [regex]::Matches($content, '"role":"assistant".*?"content":"([^"]*)"')
        
        if ($userMatches.Count -gt 0) { $lastUserMsg = $userMatches[$userMatches.Count - 1].Groups[1].Value }
        if ($assistantMatches.Count -gt 0) { $lastAssistantMsg = $assistantMatches[$assistantMatches.Count - 1].Groups[1].Value }
    } catch {}
}

# Fire-and-forget HTTP POST
Start-Job -ScriptBlock {
    param($url, $body)
    try {
        Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -TimeoutSec 2 | Out-Null
    } catch {}
} -ArgumentList "$WorkerUrl/api/sessions/summarize", (@{
    contentSessionId = $sessionId
    lastUserMessage = $lastUserMsg
    lastAssistantMessage = $lastAssistantMsg
} | ConvertTo-Json) | Out-Null

'{"continue": true, "suppressOutput": true}'
