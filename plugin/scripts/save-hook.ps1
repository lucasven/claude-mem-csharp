# PostToolUse Hook - Save tool observations

$ErrorActionPreference = "SilentlyContinue"

$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"

# Tools to skip
$SkipTools = @("ListMcpResourcesTool", "SlashCommand", "Skill", "TodoWrite", "AskUserQuestion")

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
$inputData = $inputJson | ConvertFrom-Json

$sessionId = $inputData.session_id
$cwd = if ($inputData.cwd) { $inputData.cwd } else { "." }
$toolName = if ($inputData.tool_name) { $inputData.tool_name } else { "" }
$toolInput = if ($inputData.tool_input) { $inputData.tool_input | ConvertTo-Json -Compress } else { "{}" }
$toolResponse = if ($inputData.tool_response) { $inputData.tool_response } else { "" }
$project = Split-Path -Leaf $cwd

# Skip if no session or tool name
if (-not $sessionId -or -not $toolName) {
    '{"continue": true, "suppressOutput": true}'
    exit 0
}

# Skip blocklisted tools
if ($SkipTools -contains $toolName -or $toolName -match "mcp__") {
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
elseif ($toolName -eq "Bash" -and $inputObj.command) { $title = "Bash: $($inputObj.command.Substring(0, [Math]::Min(50, $inputObj.command.Length)))" }

# Truncate response
if ($toolResponse.Length -gt 10240) {
    $toolResponse = $toolResponse.Substring(0, 10240)
}

# Fire-and-forget HTTP POST
Start-Job -ScriptBlock {
    param($url, $body)
    try {
        Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -TimeoutSec 2 | Out-Null
    } catch {}
} -ArgumentList "$WorkerUrl/api/sessions/observations", (@{
    contentSessionId = $sessionId
    toolName = $toolName
    toolInput = $toolInput
    toolResponse = $toolResponse
    title = $title
    observationType = $obsType
} | ConvertTo-Json) | Out-Null

'{"continue": true, "suppressOutput": true}'
