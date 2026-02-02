# SessionStart Hook - Inject context from previous sessions

$ErrorActionPreference = "SilentlyContinue"

$PluginRoot = if ($env:CLAUDE_PLUGIN_ROOT) { $env:CLAUDE_PLUGIN_ROOT } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$WorkerPort = if ($env:CLAUDE_MEM_WORKER_PORT) { $env:CLAUDE_MEM_WORKER_PORT } else { "37777" }
$WorkerUrl = "http://127.0.0.1:$WorkerPort"

# Read JSON input from stdin
$inputJson = [Console]::In.ReadToEnd()
$input = $inputJson | ConvertFrom-Json

$cwd = if ($input.cwd) { $input.cwd } else { "." }
$project = Split-Path -Leaf $cwd

# Ensure worker is running
& "$PSScriptRoot\ensure-worker.ps1" | Out-Null

# Fetch context from worker
try {
    $context = Invoke-RestMethod -Uri "$WorkerUrl/api/context/inject?project=$project" -TimeoutSec 10
    
    if ($context -and $context -ne "null" -and $context -ne "{}") {
        @{
            hookSpecificOutput = @{
                hookEventName = "SessionStart"
                additionalContext = $context
            }
        } | ConvertTo-Json -Depth 10
    } else {
        '{"continue": true}'
    }
} catch {
    '{"continue": true}'
}
