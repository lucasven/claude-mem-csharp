# Cross-platform hook runner for Windows
# Usage: run-hook.ps1 <hook-name>
# Example: run-hook.ps1 context-hook

param(
    [Parameter(Mandatory=$true)]
    [string]$HookName
)

$ScriptDir = $PSScriptRoot
$HookScript = Join-Path $ScriptDir "$HookName.ps1"

if (Test-Path $HookScript) {
    & $HookScript
} else {
    # Fallback - just continue
    '{"continue": true}'
}
