# Deployment Guide

Cross-platform deployment instructions for Claude-Mem Worker.

## Quick Start (All Platforms)

```bash
# Build for your platform
dotnet publish src/ClaudeMem.Worker -c Release --self-contained

# Run directly
./publish/ClaudeMem.Worker   # Linux/macOS
.\publish\ClaudeMem.Worker.exe   # Windows
```

---

## Linux

### Option 1: systemd (Recommended)

1. **Build and install:**
```bash
dotnet publish src/ClaudeMem.Worker -c Release -r linux-x64 --self-contained -o /opt/claude-mem
chmod +x /opt/claude-mem/ClaudeMem.Worker
```

2. **Install service:**
```bash
sudo cp deploy/linux/claude-mem-worker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable claude-mem-worker
sudo systemctl start claude-mem-worker
```

3. **Check status:**
```bash
sudo systemctl status claude-mem-worker
journalctl -u claude-mem-worker -f
```

### Option 2: Run manually with screen/tmux
```bash
screen -S claude-mem
/opt/claude-mem/ClaudeMem.Worker
# Ctrl+A, D to detach
```

---

## Windows

### Option 1: Task Scheduler (Simple)

1. **Build:**
```powershell
dotnet publish src/ClaudeMem.Worker -c Release -r win-x64 --self-contained -o C:\claude-mem
```

2. **Create scheduled task (run as Admin):**
```powershell
$action = New-ScheduledTaskAction -Execute "C:\claude-mem\ClaudeMem.Worker.exe"
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName "ClaudeMemWorker" -Action $action -Trigger $trigger -Settings $settings -User "SYSTEM" -RunLevel Highest
```

3. **Start immediately:**
```powershell
Start-ScheduledTask -TaskName "ClaudeMemWorker"
```

4. **Check status:**
```powershell
Get-ScheduledTask -TaskName "ClaudeMemWorker"
Get-Process ClaudeMem.Worker -ErrorAction SilentlyContinue
```

### Option 2: Windows Service (Advanced)

Use [NSSM](https://nssm.cc/) (Non-Sucking Service Manager):

```powershell
# Download NSSM and add to PATH
nssm install ClaudeMemWorker "C:\claude-mem\ClaudeMem.Worker.exe"
nssm set ClaudeMemWorker AppDirectory "C:\claude-mem"
nssm set ClaudeMemWorker DisplayName "Claude-Mem Worker"
nssm set ClaudeMemWorker Description "Persistent memory service for Claude Code"
nssm start ClaudeMemWorker
```

### Option 3: Run in PowerShell (Development)
```powershell
Start-Process -FilePath "C:\claude-mem\ClaudeMem.Worker.exe" -WindowStyle Hidden
```

---

## macOS

### Option 1: launchd (Recommended)

1. **Build:**
```bash
# Intel Mac
dotnet publish src/ClaudeMem.Worker -c Release -r osx-x64 --self-contained -o ~/Applications/claude-mem

# Apple Silicon (M1/M2/M3)
dotnet publish src/ClaudeMem.Worker -c Release -r osx-arm64 --self-contained -o ~/Applications/claude-mem

chmod +x ~/Applications/claude-mem/ClaudeMem.Worker
```

2. **Install launch agent:**
```bash
cp deploy/macos/com.claudemem.worker.plist ~/Library/LaunchAgents/
launchctl load ~/Library/LaunchAgents/com.claudemem.worker.plist
```

3. **Check status:**
```bash
launchctl list | grep claudemem
tail -f /tmp/claude-mem-worker.log
```

4. **Unload (to stop):**
```bash
launchctl unload ~/Library/LaunchAgents/com.claudemem.worker.plist
```

### Option 2: Homebrew Service (if packaging later)
```bash
brew services start claude-mem
```

---

## Docker

```bash
# Build image
docker build -t claude-mem-worker .

# Run container
docker run -d \
  --name claude-mem \
  -p 37777:37777 \
  -v claude-mem-data:/root/.claude-mem \
  claude-mem-worker
```

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CLAUDE_MEM_WORKER_PORT` | 37777 | HTTP port to listen on |
| `ASPNETCORE_URLS` | http://127.0.0.1:37777 | Full URL binding |

---

## Data Location

The SQLite database is stored at:

| Platform | Path |
|----------|------|
| Linux | `~/.claude-mem/claude-mem.db` |
| macOS | `~/.claude-mem/claude-mem.db` |
| Windows | `C:\Users\<user>\.claude-mem\claude-mem.db` |

---

## Troubleshooting

### Port already in use
```bash
# Linux/macOS
lsof -i :37777
kill -9 <PID>

# Windows
netstat -ano | findstr :37777
taskkill /PID <PID> /F
```

### Check if running
```bash
curl http://127.0.0.1:37777/health
# Should return: {"status":"healthy"}
```

### View logs
```bash
# Linux (systemd)
journalctl -u claude-mem-worker -f

# macOS (launchd)
tail -f /tmp/claude-mem-worker.log

# Windows (Event Viewer or NSSM logs)
nssm status ClaudeMemWorker
```
