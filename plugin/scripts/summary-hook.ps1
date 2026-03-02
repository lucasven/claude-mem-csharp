# Stop Hook - Generate session summary

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [summary-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

Write-Log "Hook started"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
Write-Log "Input: $inputJson"

$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$transcriptPath = $inputData.transcript_path

Write-Log "SessionId: $sessionId, Transcript: $transcriptPath"

# Skip if no session
if (-not $sessionId) {
    Write-Log "No session_id, skipping"
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Extract last messages from transcript
$lastUserMsg = ""
$lastAssistantMsg = ""

if ($transcriptPath -and (Test-Path $transcriptPath)) {
    Write-Log "Reading transcript from $transcriptPath"
    try {
        $content = Get-Content $transcriptPath -Raw
        $userMatches = [regex]::Matches($content, '"role":"user".*?"content":"([^"]*)"')
        $assistantMatches = [regex]::Matches($content, '"role":"assistant".*?"content":"([^"]*)"')
        
        if ($userMatches.Count -gt 0) { $lastUserMsg = $userMatches[$userMatches.Count - 1].Groups[1].Value }
        if ($assistantMatches.Count -gt 0) { $lastAssistantMsg = $assistantMatches[$assistantMatches.Count - 1].Groups[1].Value }
        Write-Log "Found $($userMatches.Count) user messages, $($assistantMatches.Count) assistant messages"
    } catch {
        Write-Log "Error reading transcript: $($_.Exception.Message)"
    }
} else {
    Write-Log "No transcript file found"
}

# HTTP POST
try {
    $body = @{
        contentSessionId = $sessionId
        lastUserMessage = $lastUserMsg
        lastAssistantMessage = $lastAssistantMsg
    } | ConvertTo-Json
    
    Write-Log "POST $WorkerUrl/api/sessions/summarize"
    $response = Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/summarize" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    Write-Log "Response: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Log "Error: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
