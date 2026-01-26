# Claude-Mem .NET

A .NET 9 port of [claude-mem](https://github.com/thedotmack/claude-mem) - the persistent memory compression system for Claude Code.

## Purpose

This is a clean-room .NET implementation of claude-mem, created for improved stability and maintainability. The original TypeScript/Bun implementation works well but can experience stability issues on certain platforms. This .NET port provides:

- **Cross-platform stability** - .NET 9 runtime with consistent behavior across Windows, macOS, and Linux
- **Native AOT compilation** - Faster startup times for CLI hooks
- **SQLite with Microsoft.Data.Sqlite** - Battle-tested database layer
- **ASP.NET Core Minimal APIs** - Lightweight HTTP worker service

## Architecture

```
claude-mem-csharp/
├── src/
│   ├── ClaudeMem.Core/           # Domain models, repositories, database
│   ├── ClaudeMem.Worker/         # HTTP API service (ASP.NET Core)
│   └── ClaudeMem.Hooks/          # CLI hooks for Claude Code integration
└── tests/
    └── ClaudeMem.Core.Tests/     # xUnit tests
```

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Building

```bash
# Restore and build
dotnet build

# Run tests
dotnet test
```

## Deployment

### Worker Service

The Worker is an HTTP API that handles memory persistence and search.

```bash
# Run the worker (default port: 37778)
dotnet run --project src/ClaudeMem.Worker

# Or with custom port
CLAUDE_MEM_WORKER_PORT=38888 dotnet run --project src/ClaudeMem.Worker
```

### Publishing for Production

```bash
# Publish self-contained executables
dotnet publish src/ClaudeMem.Worker -c Release -r win-x64 --self-contained
dotnet publish src/ClaudeMem.Hooks -c Release -r win-x64 --self-contained

# For Linux
dotnet publish src/ClaudeMem.Worker -c Release -r linux-x64 --self-contained
dotnet publish src/ClaudeMem.Hooks -c Release -r linux-x64 --self-contained

# For macOS
dotnet publish src/ClaudeMem.Worker -c Release -r osx-x64 --self-contained
dotnet publish src/ClaudeMem.Hooks -c Release -r osx-arm64 --self-contained
```

### Native AOT (Faster Startup)

For the Hooks CLI, Native AOT compilation provides near-instant startup:

```bash
dotnet publish src/ClaudeMem.Hooks -c Release -r win-x64 -p:PublishAot=true
```

### Running as a Service

**Windows (Task Scheduler or Windows Service):**
```powershell
# Create a scheduled task to run at startup
$action = New-ScheduledTaskAction -Execute "C:\path\to\ClaudeMem.Worker.exe"
$trigger = New-ScheduledTaskTrigger -AtStartup
Register-ScheduledTask -TaskName "ClaudeMemWorker" -Action $action -Trigger $trigger
```

**Linux (systemd):**
```ini
# /etc/systemd/system/claude-mem-worker.service
[Unit]
Description=Claude-Mem Worker Service
After=network.target

[Service]
Type=simple
ExecStart=/opt/claude-mem/ClaudeMem.Worker
Restart=always
Environment=CLAUDE_MEM_WORKER_PORT=37778

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable claude-mem-worker
sudo systemctl start claude-mem-worker
```

## Web Viewer UI

The .NET port includes the same web viewer UI as the original TypeScript version.

### Accessing the Viewer

1. Start the Worker service:
   ```bash
   dotnet run --project src/ClaudeMem.Worker
   ```

2. Open browser to: `http://localhost:37778`

### Features

- Real-time observation feed with SSE updates
- Project filtering dropdown
- Observation, summary, and prompt cards
- Dark/light theme toggle
- Settings modal for context configuration
- Console logs drawer

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Viewer HTML page |
| `/stream` | GET | SSE stream for real-time updates |
| `/health` | GET | Health check |
| `/api/stats` | GET | Worker and database statistics |
| `/api/projects` | GET | List of distinct projects |
| `/api/observations` | GET | List observations (paginated) |
| `/api/observation/{id}` | GET | Get observation by ID |
| `/api/observations/batch` | POST | Batch get observations |
| `/api/summaries` | GET | List summaries (paginated) |
| `/api/summary/{id}` | GET | Get summary by ID |
| `/api/prompts` | GET | List user prompts (paginated) |
| `/api/prompt/{id}` | GET | Get prompt by ID |
| `/api/sessions/init` | POST | Initialize session |
| `/api/sessions/observations` | POST | Queue observation |
| `/api/sessions/summarize` | POST | Queue summary |
| `/api/processing-status` | GET | Processing queue status |
| `/api/processing` | POST | Trigger status broadcast |

## Database

SQLite database is stored at `~/.claude-mem/claude-mem.db` with:
- WAL mode for concurrent access
- FTS5 full-text search (planned)
- Automatic migrations

## License

This project is licensed under the GNU Affero General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

Based on [claude-mem](https://github.com/thedotmack/claude-mem) by Alex Newman (@thedotmack).
