# Claude-Mem Worker Installation Script for Windows
# Run as Administrator

param(
    [string]$InstallPath = "C:\claude-mem",
    [int]$Port = 37777
)

$ErrorActionPreference = "Stop"

Write-Host "Claude-Mem Worker Installation" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script as Administrator"
    exit 1
}

# Create install directory
if (-not (Test-Path $InstallPath)) {
    Write-Host "Creating directory: $InstallPath"
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Check if executable exists
$exePath = Join-Path $InstallPath "ClaudeMem.Worker.exe"
if (-not (Test-Path $exePath)) {
    Write-Host ""
    Write-Host "Executable not found at: $exePath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please build first:" -ForegroundColor Yellow
    Write-Host "  dotnet publish src/ClaudeMem.Worker -c Release -r win-x64 --self-contained -o $InstallPath"
    Write-Host ""
    exit 1
}

# Remove existing task if present
$taskName = "ClaudeMemWorker"
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Write-Host "Removing existing scheduled task..."
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

# Create new scheduled task
Write-Host "Creating scheduled task..."

$action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Days 365)

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -User "SYSTEM" `
    -RunLevel Highest `
    -Description "Persistent memory service for Claude Code" | Out-Null

# Start the task
Write-Host "Starting service..."
Start-ScheduledTask -TaskName $taskName

# Wait and verify
Start-Sleep -Seconds 3

$process = Get-Process -Name "ClaudeMem.Worker" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host ""
    Write-Host "Installation successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service is running on: http://127.0.0.1:$Port"
    Write-Host ""
    Write-Host "Verify with:"
    Write-Host "  curl http://127.0.0.1:$Port/health"
    Write-Host ""
    Write-Host "To uninstall:"
    Write-Host "  Unregister-ScheduledTask -TaskName 'ClaudeMemWorker' -Confirm:`$false"
} else {
    Write-Host ""
    Write-Host "Warning: Process may not have started correctly" -ForegroundColor Yellow
    Write-Host "Check Task Scheduler for details"
}
