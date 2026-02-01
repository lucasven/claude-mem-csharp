# Claude-Mem .NET

A .NET 9 port of [claude-mem](https://github.com/thedotmack/claude-mem) - the persistent memory compression system for Claude Code.

## Purpose

This is a clean-room .NET implementation of claude-mem, created for improved stability and maintainability. The original TypeScript/Bun implementation works well but can experience stability issues on certain platforms. This .NET port provides:

> **Note:** The original claude-mem's feature that auto-generates `CLAUDE.md` files in project directories has been **intentionally removed**. This port only stores data in `~/.claude-mem/` and never writes files to your project folders.

- **Cross-platform stability** - .NET 9 runtime with consistent behavior across Windows, macOS, and Linux
- **Native AOT compilation** - Faster startup times for CLI hooks
- **SQLite with Microsoft.Data.Sqlite** - Battle-tested database layer
- **ASP.NET Core Minimal APIs** - Lightweight HTTP worker service
- **Hybrid Search** - FTS5 keyword + vector semantic search combined

## Architecture

```
claude-mem-csharp/
├── src/
│   ├── ClaudeMem.Core/           # Domain models, repositories, database
│   │   ├── Data/                 # SQLite + migrations (including FTS5)
│   │   ├── Services/             # Search services (FTS5, Hybrid, Vector)
│   │   └── Repositories/         # Data access layer
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

## Search Architecture

This port implements a **3-layer workflow** for token-efficient search:

```
Layer 1: /api/search     → Get index with IDs (~50-100 tokens/result)
Layer 2: /api/timeline   → Get chronological context around results
Layer 3: /api/observations/batch → Fetch full details for selected IDs
```

### Hybrid Search

Combines two retrieval methods for best results:

- **FTS5 (SQLite Full-Text Search)** - Fast keyword matching, great for exact terms, IDs, code symbols
- **Vector Similarity** - Semantic matching, finds related content even with different wording

Weighted scoring merges results: `score = (vectorWeight × vectorScore) + (textWeight × ftsScore)`

Default weights: 70% vector, 30% FTS5 (configurable)

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
dotnet publish src/ClaudeMem.Worker -c Release -r linux-x64 --self-contained
dotnet publish src/ClaudeMem.Hooks -c Release -r linux-x64 --self-contained

# For macOS
dotnet publish src/ClaudeMem.Worker -c Release -r osx-arm64 --self-contained

# For Windows
dotnet publish src/ClaudeMem.Worker -c Release -r win-x64 --self-contained
```

### Running as a Service

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

### Search (3-Layer Workflow)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/search` | GET | Hybrid search (FTS5 + vector) |
| `/api/timeline` | GET | Chronological context around an observation |
| `/api/observations/batch` | POST | Fetch full details by IDs |
| `/api/search/status` | GET | Search service status |
| `/api/search/rebuild-fts` | POST | Rebuild FTS5 index |

### Sessions & Observations

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/sessions/init` | POST | Initialize session |
| `/api/sessions/observations` | POST | Store observation (auto-indexes) |
| `/api/sessions/summarize` | POST | Store summary |
| `/api/observations` | GET | List observations (paginated) |
| `/api/observation/{id}` | GET | Get single observation |
| `/api/processing-status` | GET | Queue status |

### Health

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/stats` | GET | Worker statistics |

## Configuration

### Environment Variables

```bash
# Worker port (default: 37777)
CLAUDE_MEM_WORKER_PORT=37777

# Project name for collections
CLAUDE_MEM_PROJECT=my-project

# Vector search (disabled by default - FTS5 always available)
CLAUDE_MEM_VECTOR_ENABLED=true

# OpenAI embeddings (required if vector enabled)
CLAUDE_MEM_EMBEDDING_PROVIDER=openai
CLAUDE_MEM_EMBEDDING_API_KEY=sk-...
CLAUDE_MEM_EMBEDDING_MODEL=text-embedding-3-small
CLAUDE_MEM_EMBEDDING_BASE_URL=https://api.openai.com/v1/  # optional

# Vector store: sqlite (default), qdrant
CLAUDE_MEM_VECTOR_STORE=sqlite
QDRANT_URL=http://localhost:6333  # if using qdrant
```

### Recommended Setups

**Option 1: FTS5 Only (Zero Dependencies)**
```bash
# No configuration needed - FTS5 search works out of the box
# Great for keyword search, code symbols, exact matches
```

**Option 2: Hybrid with OpenAI (Best Quality)**
```bash
CLAUDE_MEM_VECTOR_ENABLED=true
CLAUDE_MEM_EMBEDDING_API_KEY=sk-your-openai-key
# Uses OpenAI text-embedding-3-small + SQLite vector store
# Best semantic understanding
```

**Option 3: Hybrid with OpenRouter (Cost Effective)**
```bash
CLAUDE_MEM_VECTOR_ENABLED=true
CLAUDE_MEM_EMBEDDING_API_KEY=sk-or-your-openrouter-key
CLAUDE_MEM_EMBEDDING_BASE_URL=https://openrouter.ai/api/v1/
CLAUDE_MEM_EMBEDDING_MODEL=openai/text-embedding-3-small
```

## API Usage Examples

### Search Workflow

```bash
# Step 1: Search for index
curl "http://localhost:37777/api/search?query=authentication%20bug&limit=10"
# Response: { "results": [{ "observationId": 123, "score": 0.85, ... }] }

# Step 2: Get timeline context for interesting result
curl "http://localhost:37777/api/timeline?anchor=123&depthBefore=3&depthAfter=3"
# Response: { "before": [...], "anchor": {...}, "after": [...] }

# Step 3: Fetch full details for selected observations
curl -X POST "http://localhost:37777/api/observations/batch" \
  -H "Content-Type: application/json" \
  -d '{"ids": [123, 125, 127]}'
```

### Store Observations

```bash
# Initialize session
curl -X POST http://localhost:37777/api/sessions/init \
  -H "Content-Type: application/json" \
  -d '{
    "contentSessionId": "session-123",
    "project": "/path/to/project"
  }'

# Store observation (auto-indexed for both FTS5 and vector)
curl -X POST http://localhost:37777/api/sessions/observations \
  -H "Content-Type: application/json" \
  -d '{
    "contentSessionId": "session-123",
    "toolName": "read_file",
    "toolResponse": "file contents...",
    "title": "Read config file",
    "observationType": "discovery"
  }'
# Response: {"status":"stored","observationId":1,"ftsIndexed":true,"vectorIndexing":true}
```

## Database

SQLite database at `~/.claude-mem/claude-mem.db`:
- **WAL mode** for concurrent access
- **FTS5 virtual tables** for full-text search
- **Automatic migrations** on startup
- **Auto-sync triggers** keep FTS5 index updated

## License

This project is licensed under the GNU Affero General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

Based on [claude-mem](https://github.com/thedotmack/claude-mem) by Alex Newman (@thedotmack).
