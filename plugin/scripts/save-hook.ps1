# PostToolUse Hook - Save tool observations

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [save-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

Write-Log "Hook started"

# Tools to skip
$SkipTools = @("ListMcpResourcesTool", "SlashCommand", "Skill", "TodoWrite", "AskUserQuestion")

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
Write-Log "Input length: $($inputJson.Length) chars"

$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$cwd = if ($inputData.cwd) { $inputData.cwd } else { "." }
$toolName = if ($inputData.tool_name) { $inputData.tool_name } else { "" }
$toolInput = if ($inputData.tool_input) { $inputData.tool_input | ConvertTo-Json -Compress -Depth 5 } else { "{}" }
$toolResponse = if ($inputData.tool_response) { 
    $resp = $inputData.tool_response
    if ($resp -is [string]) { $resp } else { $resp | ConvertTo-Json -Compress -Depth 5 }
} else { "" }
$project = Split-Path -Leaf $cwd

Write-Log "SessionId: $sessionId, Tool: $toolName, Project: $project"

# Skip if no session or tool name
if (-not $sessionId -or -not $toolName) {
    Write-Log "No session_id or tool_name, skipping"
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Skip blocklisted tools
if ($SkipTools -contains $toolName -or $toolName -match "mcp__") {
    Write-Log "Tool $toolName is blocklisted, skipping"
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Determine observation type
$obsType = switch -Regex ($toolName) {
    "Read|Grep|Glob|Search" { "discovery" }
    "Write|Edit|apply_patch" { "modification" }
    "Bash|exec|process" { "action" }
    default { "observation" }
}

# Generate title
$title = $toolName
$inputObj = $inputData.tool_input
if ($toolName -eq "Read" -and $inputObj.file_path) { $title = "Read: $($inputObj.file_path)" }
elseif ($toolName -eq "Read" -and $inputObj.path) { $title = "Read: $($inputObj.path)" }
elseif ($toolName -eq "Write" -and $inputObj.file_path) { $title = "Write: $($inputObj.file_path)" }
elseif ($toolName -eq "Write" -and $inputObj.path) { $title = "Write: $($inputObj.path)" }
elseif ($toolName -eq "Bash" -and $inputObj.command) { 
    $cmdPreview = $inputObj.command.Substring(0, [Math]::Min(50, $inputObj.command.Length))
    $title = "Bash: $cmdPreview" 
}

Write-Log "Title: $title, Type: $obsType"

# Truncate response
if ($toolResponse.Length -gt 10240) {
    $toolResponse = $toolResponse.Substring(0, 10240)
    Write-Log "Response truncated to 10KB"
}

# HTTP POST (synchronous for debugging)
try {
    $body = @{
        contentSessionId = $sessionId
        toolName = $toolName
        toolInput = $toolInput
        toolResponse = $toolResponse
        title = $title
        observationType = $obsType
    } | ConvertTo-Json -Depth 5
    
    Write-Log "POST $WorkerUrl/api/sessions/observations"
    $response = Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/observations" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    Write-Log "Response: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Log "Error: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
