# SessionStart Hook - Inject context from previous sessions

$ErrorActionPreference = "SilentlyContinue"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"
$DataDir = Join-Path $env:USERPROFILE ".claude-mem-csharp"
$LogFile = Join-Path $DataDir "hooks.log"

function Write-Log {
    param($Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [context-hook] $Message" | Add-Content -Path $LogFile -ErrorAction SilentlyContinue
}

# Ensure log directory exists
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

Write-Log "Hook started"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
Write-Log "Input: $inputJson"

$input = $inputJson | ConvertFrom-Json

$cwd = if ($input.cwd) { $input.cwd } else { "." }
$project = Split-Path -Leaf $cwd

Write-Log "Project: $project, CWD: $cwd"

# Ensure worker is running
Write-Log "Ensuring worker is running..."
& "$PSScriptRoot\ensure-worker.ps1" | Out-Null

# Fetch context from worker
try {
    Write-Log "GET $WorkerUrl/api/context/inject?project=$project"
    $context = Invoke-RestMethod -Uri "$WorkerUrl/api/context/inject?project=$project" -TimeoutSec 10
    Write-Log "Context length: $($context.Length) chars"
    
    if ($context -and $context -ne "null" -and $context -ne "{}") {
        $output = @{
            hookSpecificOutput = @{
                hookEventName = "SessionStart"
                additionalContext = $context
            }
        } | ConvertTo-Json -Depth 10
        Write-Log "Returning context"
        $output
    } else {
        Write-Log "No context to inject"
        '{"continue": true}'
    }
} catch {
    Write-Log "Error: $($_.Exception.Message)"
    '{"continue": true}'
}

Write-Log "Hook completed"
