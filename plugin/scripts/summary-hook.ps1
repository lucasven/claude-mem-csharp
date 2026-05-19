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

# Ensure worker is running
& "$PSScriptRoot\ensure-worker.ps1" | Out-Null

# Extract last messages from transcript using proper JSON parsing
$lastUserMsg = ""
$lastAssistantMsg = ""

if ($transcriptPath -and (Test-Path $transcriptPath)) {
    Write-Log "Reading transcript from $transcriptPath"
    try {
        # Transcript is JSONL (one JSON object per line)
        $lines = Get-Content $transcriptPath -ErrorAction Stop

        foreach ($line in $lines) {
            if (-not $line.Trim()) { continue }
            try {
                $entry = $line | ConvertFrom-Json -ErrorAction Stop

                if ($entry.type -eq "human" -or $entry.role -eq "user") {
                    # Extract text content from message
                    $text = ""
                    if ($entry.message -and $entry.message.content) {
                        foreach ($block in $entry.message.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $text += $block.text
                            }
                        }
                    }
                    elseif ($entry.content -is [string]) {
                        $text = $entry.content
                    }
                    elseif ($entry.content -is [array]) {
                        foreach ($block in $entry.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $text += $block.text
                            }
                        }
                    }
                    if ($text) { $lastUserMsg = $text }
                }
                elseif ($entry.type -eq "assistant" -or $entry.role -eq "assistant") {
                    $text = ""
                    if ($entry.message -and $entry.message.content) {
                        foreach ($block in $entry.message.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $text += $block.text
                            }
                        }
                    }
                    elseif ($entry.content -is [string]) {
                        $text = $entry.content
                    }
                    elseif ($entry.content -is [array]) {
                        foreach ($block in $entry.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $text += $block.text
                            }
                        }
                    }
                    if ($text) { $lastAssistantMsg = $text }
                }
            } catch {
                # Skip unparseable lines
            }
        }

        # Truncate to avoid huge payloads (keep last 500 chars)
        if ($lastUserMsg.Length -gt 500) {
            $lastUserMsg = $lastUserMsg.Substring($lastUserMsg.Length - 500)
        }
        if ($lastAssistantMsg.Length -gt 500) {
            $lastAssistantMsg = $lastAssistantMsg.Substring($lastAssistantMsg.Length - 500)
        }

        Write-Log "Extracted user msg ($(($lastUserMsg).Length) chars), assistant msg ($(($lastAssistantMsg).Length) chars)"
    } catch {
        Write-Log "Error reading transcript: $($_.Exception.Message)"
    }
} else {
    Write-Log "No transcript file found"
}

# HTTP POST - increased timeout since summarize does more work now
try {
    $body = @{
        contentSessionId = $sessionId
        lastUserMessage = $lastUserMsg
        lastAssistantMessage = $lastAssistantMsg
    } | ConvertTo-Json

    Write-Log "POST $WorkerUrl/api/sessions/summarize"
    $response = Invoke-RestMethod -Uri "$WorkerUrl/api/sessions/summarize" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 15
    Write-Log "Response: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Log "Error: $($_.Exception.Message)"
}

Write-Log "Hook completed"
'{"continue": true, "suppressOutput": true}'
