# Claude-Mem C#

A .NET 9 port of [claude-mem](https://github.com/thedotmack/claude-mem) - the persistent memory compression system for Claude Code.

## Purpose

This is a clean-room .NET implementation of claude-mem, created for improved stability and maintainability. The original TypeScript/Bun implementation works well but can experience stability issues on certain platforms. This .NET port provides:

- **Self-contained executables** - No runtime dependencies, works on any system
- **Cross-platform support** - Windows, macOS, and Linux (x64 and ARM64)
- **Claude Code plugin** - Install directly via the plugin marketplace
- **SQLite with Microsoft.Data.Sqlite** - Battle-tested database layer
- **ASP.NET Core Minimal APIs** - Lightweight HTTP worker service
- **MCP Server** - Memory tools accessible directly in Claude Code

## Installation

### Option 1: Claude Code Plugin (Recommended)

Install directly in Claude Code with no external dependencies:

```
/plugin marketplace add lucasven/claude-mem-csharp
/plugin install claude-mem-csharp@claude-mem-csharp-marketplace
```

The plugin will automatically download pre-built binaries for your platform.

### Option 2: One-Line Install Script

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/lucasven/claude-mem-csharp/main/install.ps1 | iex
```

**macOS / Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/lucasven/claude-mem-csharp/main/install.sh | bash
```

The installer will:
1. Download pre-built binaries for your platform
2. Install to `~/.claude-mem-csharp/`
3. Provide instructions for plugin or manual setup

### Option 3: Manual Download

Download the latest release from [GitHub Releases](https://github.com/lucasven/claude-mem-csharp/releases):

| Platform | Download |
|----------|----------|
| Windows x64 | `claude-mem-csharp-win-x64.zip` |
| Windows ARM64 | `claude-mem-csharp-win-arm64.zip` |
| Linux x64 | `claude-mem-csharp-linux-x64.tar.gz` |
| Linux ARM64 | `claude-mem-csharp-linux-arm64.tar.gz` |
| macOS x64 (Intel) | `claude-mem-csharp-osx-x64.tar.gz` |
| macOS ARM64 (Apple Silicon) | `claude-mem-csharp-osx-arm64.tar.gz` |

Extract and run `./claude-mem-csharp worker start` to start the service.

## Build from Source

If you prefer to build from source, you'll need .NET 9 SDK:

```bash
# Clone with submodules
git clone --recurse-submodules https://github.com/lucasven/claude-mem-csharp.git
cd claude-mem-csharp

# Build for current platform
./build.sh  # or build.ps1 on Windows

# Build for all platforms
./build.sh all

# Run tests
dotnet test
```

## CLI Commands

The `claude-mem-csharp` CLI provides commands for managing the memory system:

```bash
# Worker service management
claude-mem-csharp worker start [--port 37777] [--foreground]
claude-mem-csharp worker stop
claude-mem-csharp worker status
claude-mem-csharp worker restart

# Check installation status
claude-mem-csharp status

# Hook handler (called by Claude Code)
claude-mem-csharp hook <platform> <event>
```

## Architecture

```
claude-mem-csharp/
├── .claude-plugin/              # Plugin manifest and marketplace config
│   ├── plugin.json             # Plugin definition
│   └── marketplace.json        # Marketplace catalog
├── hooks/                       # Claude Code hook configurations
│   └── hooks.json
├── .mcp.json                    # MCP server configuration
├── bin/                         # Pre-built binaries (per platform)
├── src/
│   ├── ClaudeMem.Core/          # Domain models, repositories, database
│   ├── ClaudeMem.Worker/        # HTTP API service (ASP.NET Core)
│   ├── ClaudeMem.Hooks/         # CLI for hooks and management
│   └── ClaudeMem.Mcp/           # MCP server for Claude Code tools
├── lib/
│   └── claude-agent-sdk-dotnet/ # Claude Agent SDK (submodule)
└── tests/
    └── ClaudeMem.Core.Tests/    # xUnit tests
```

## How It Works

### Hooks

Claude Code hooks are configured to call the CLI at specific events:

- **PreToolUse** - Injects relevant memory context before tool execution
- **PostToolUse** - Captures observations from tool interactions
- **Stop** - Generates session summaries when conversation ends

### MCP Server

The MCP server exposes memory tools directly to Claude:

| Tool | Description |
|------|-------------|
| `memory_search` | Search through stored observations |
| `memory_get_context` | Get relevant context for a project |
| `memory_get_observations` | List recent observations |
| `memory_get_summaries` | List session summaries |

### Worker Service

The Worker is a background HTTP service that:

- Processes observation extraction using Claude
- Generates session summaries
- Manages the SQLite database
- Provides REST API for memory operations

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

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CLAUDE_MEM_WORKER_PORT` | `37777` | Worker HTTP port |
| `CLAUDE_MEM_DATA_DIR` | `~/.claude-mem-csharp` | Data directory |

## Database

SQLite database is stored at `~/.claude-mem-csharp/claude-mem-csharp.db` with:

- WAL mode for concurrent access
- Automatic migrations
- Tables: `observations`, `sessions`, `summaries`

## Running Worker as a System Service

**Windows (Task Scheduler):**
```powershell
$action = New-ScheduledTaskAction -Execute "$env:USERPROFILE\.claude-mem-csharp\ClaudeMem.Worker.exe"
$trigger = New-ScheduledTaskTrigger -AtStartup
Register-ScheduledTask -TaskName "ClaudeMemWorker" -Action $action -Trigger $trigger
```

**Linux (systemd):**
```ini
# /etc/systemd/system/claude-mem-csharp.service
[Unit]
Description=Claude-Mem Worker Service
After=network.target

[Service]
Type=simple
ExecStart=/home/user/.claude-mem-csharp/ClaudeMem.Worker
Restart=always
Environment=CLAUDE_MEM_WORKER_PORT=37777

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable claude-mem-csharp
sudo systemctl start claude-mem-csharp
```

## Troubleshooting

### Check Status

```bash
claude-mem-csharp status
```

### View Worker Logs

```bash
# Run in foreground to see logs
claude-mem-csharp worker start --foreground
```

### Reset Installation

```bash
claude-mem-csharp uninstall --global
claude-mem-csharp install --global --force
```

## License

This project is licensed under the GNU Affero General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

Based on [claude-mem](https://github.com/thedotmack/claude-mem) by Alex Newman (@thedotmack).
