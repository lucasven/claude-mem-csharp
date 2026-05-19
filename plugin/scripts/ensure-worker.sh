#!/bin/bash
# Ensure the claude-mem-csharp Worker is running
# Called by all hooks before making HTTP requests

set -e

WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"
DATA_DIR="${HOME}/.claude-mem-csharp"
PID_FILE="${DATA_DIR}/worker.pid"
LOG_FILE="${DATA_DIR}/worker.log"
WORKER_DIR="${DATA_DIR}/worker"

# Create data directory
mkdir -p "${DATA_DIR}"

# Find source root for building
find_source_root() {
    # 1. Explicit env var
    if [ -n "$CLAUDE_MEM_SOURCE_ROOT" ] && [ -d "$CLAUDE_MEM_SOURCE_ROOT" ]; then
        echo "$CLAUDE_MEM_SOURCE_ROOT"
        return 0
    fi

    # 2. Derive from cache path: .../plugins/cache/lucasven/... -> .../plugins/marketplaces/lucasven/
    PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-}"
    if [ -n "$PLUGIN_ROOT" ]; then
        MARKETPLACE_PATH=$(echo "$PLUGIN_ROOT" | sed -E 's|(.*[/\\]plugins[/\\])cache[/\\]([^/\\]+).*|\1marketplaces/\2|')
        if [ "$MARKETPLACE_PATH" != "$PLUGIN_ROOT" ] && [ -f "${MARKETPLACE_PATH}/ClaudeMem.sln" ]; then
            echo "$MARKETPLACE_PATH"
            return 0
        fi
    fi

    # 3. Check common marketplace locations
    MARKETPLACES_DIR="${HOME}/.claude/plugins/marketplaces"
    if [ -d "$MARKETPLACES_DIR" ]; then
        for dir in "$MARKETPLACES_DIR"/*/; do
            if [ -f "${dir}ClaudeMem.sln" ]; then
                echo "${dir%/}"
                return 0
            fi
        done
    fi

    return 1
}

# Check if worker is already running
check_worker() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE" 2>/dev/null || echo "")
        if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
            if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
                return 0
            fi
        fi
    fi
    # Also try health check without PID
    if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
        return 0
    fi
    return 1
}

# Install .NET SDK
install_dotnet() {
    echo "Installing .NET 9.0 SDK..." >&2
    OS="$(uname -s)"
    case "$OS" in
        Darwin)
            if command -v brew &> /dev/null; then
                brew install dotnet@9 2>&1 | tail -3 >&2
                brew link --overwrite dotnet@9 2>/dev/null || true
            else
                curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
                export PATH="$HOME/.dotnet:$PATH"
            fi
            ;;
        Linux)
            curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
            export PATH="$HOME/.dotnet:$PATH"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            if command -v winget &> /dev/null; then
                winget install Microsoft.DotNet.SDK.9 --silent 2>&1 | tail -3 >&2
            else
                echo "Error: Please install .NET 9.0 SDK manually." >&2
                exit 1
            fi
            ;;
        *)
            echo "Error: Unsupported OS. Install .NET 9.0 SDK manually." >&2
            exit 1
            ;;
    esac
}

# Start the worker
start_worker() {
    # Check if dotnet is available, install if not
    if ! command -v dotnet &> /dev/null; then
        if [ -x "$HOME/.dotnet/dotnet" ]; then
            export PATH="$HOME/.dotnet:$PATH"
        else
            install_dotnet
        fi
    fi

    PUBLISHED_DLL="${WORKER_DIR}/ClaudeMem.Worker.dll"

    if [ ! -f "$PUBLISHED_DLL" ]; then
        SOURCE_ROOT=$(find_source_root)
        if [ -z "$SOURCE_ROOT" ]; then
            echo "Error: Cannot find claude-mem-csharp source. Set CLAUDE_MEM_SOURCE_ROOT env var." >&2
            exit 1
        fi

        echo "Building claude-mem-csharp from ${SOURCE_ROOT}..." >&2
        dotnet publish "${SOURCE_ROOT}/src/ClaudeMem.Worker" -c Release -o "$WORKER_DIR" --nologo -v q >/dev/null 2>&1

        if [ ! -f "$PUBLISHED_DLL" ]; then
            echo "Error: Build failed. Check source at ${SOURCE_ROOT}" >&2
            exit 1
        fi
    fi

    # Start worker in background using the published DLL
    nohup dotnet "$PUBLISHED_DLL" > "$LOG_FILE" 2>&1 &
    echo $! > "$PID_FILE"

    # Wait for worker to be ready (max 30 seconds)
    for i in $(seq 1 30); do
        if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done

    echo "Error: Worker failed to start. Check ${LOG_FILE}" >&2
    exit 1
}

# Main
if ! check_worker; then
    start_worker
fi

echo "${WORKER_URL}"
