# Claude-Mem .NET

A .NET 9 port of [claude-mem](https://github.com/thedotmack/claude-mem) - the persistent memory compression system for Claude Code.

## Purpose

This is a clean-room .NET implementation of claude-mem, created for improved stability and maintainability. The original TypeScript/Bun implementation works well but can experience stability issues on certain platforms. This .NET port provides:

> **Note:** The original claude-mem's feature that auto-generates `CLAUDE.md` files in project directories has been **intentionally removed**. This port only stores data in `~/.claude-mem/` and never writes files to your project folders.

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
- Python 3.10+ (for ChromaDB semantic search)
- [uv](https://docs.astral.sh/uv/) package manager (optional, for running chroma-mcp)

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
# Run the worker (default port: 37777)
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
Environment=CLAUDE_MEM_WORKER_PORT=37777

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable claude-mem-worker
sudo systemctl start claude-mem-worker
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/stats` | GET | Worker statistics |
| `/api/observations` | GET | List observations (paginated) |
| `/api/observation/{id}` | GET | Get observation by ID |
| `/api/observations/batch` | POST | Batch get observations |
| `/api/sessions/init` | POST | Initialize session |
| `/api/sessions/observations` | POST | Queue observation |
| `/api/sessions/summarize` | POST | Queue summary |
| `/api/processing-status` | GET | Queue status |

### API Usage Examples

**Initialize a session:**
```bash
curl -X POST http://localhost:37777/api/sessions/init \
  -H "Content-Type: application/json" \
  -d '{
    "contentSessionId": "my-session-123",
    "project": "/path/to/project",
    "prompt": "Initial task description"
  }'
# Response: {"sessionDbId":1,"promptNumber":1,"skipped":false}
```

**Queue an observation:**
```bash
curl -X POST http://localhost:37777/api/sessions/observations \
  -H "Content-Type: application/json" \
  -d '{
    "contentSessionId": "my-session-123",
    "toolName": "read_file",
    "toolInput": {"path": "/etc/hosts"},
    "toolResponse": "...",
    "cwd": "/home/user"
  }'
# Response: {"status":"queued"}
```

**Request summary generation:**
```bash
curl -X POST http://localhost:37777/api/sessions/summarize \
  -H "Content-Type: application/json" \
  -d '{
    "contentSessionId": "my-session-123",
    "lastAssistantMessage": "Task completed successfully"
  }'
# Response: {"status":"queued"}
```

**Check health and stats:**
```bash
curl http://localhost:37777/health
# Response: {"status":"healthy"}

curl http://localhost:37777/api/stats
# Response: {"worker":{"version":"1.0.0","uptime":12345,"port":37777},"database":{"observations":42}}
```

## Database

SQLite database is stored at `~/.claude-mem/claude-mem.db` with:
- WAL mode for concurrent access
- Automatic migrations

## Semantic Search (ChromaDB)

This port includes optional semantic search via ChromaDB:
- Uses `chroma-mcp` (MCP server) for vector embeddings
- Default embedding model: `all-MiniLM-L6-v2` (~80MB, runs on CPU)
- Vector database stored at `~/.claude-mem/vector-db/`

**Enable/disable via environment:**
```bash
# Enabled by default
CLAUDE_MEM_CHROMA_ENABLED=true

# Disable if you don't need semantic search
CLAUDE_MEM_CHROMA_ENABLED=false

# Python version for chroma-mcp (default: 3.12)
CLAUDE_MEM_PYTHON_VERSION=3.12
```

**Search endpoint:**
```bash
curl "http://localhost:37777/api/search?query=authentication%20bug&limit=5"
```

## License

This project is licensed under the GNU Affero General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

Based on [claude-mem](https://github.com/thedotmack/claude-mem) by Alex Newman (@thedotmack).
