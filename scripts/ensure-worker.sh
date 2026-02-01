#!/bin/bash
# Ensure the claude-mem-csharp Worker is running
# Called by all hooks before making HTTP requests

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"
PID_FILE="${HOME}/.claude-mem/worker.pid"
LOG_FILE="${HOME}/.claude-mem/worker.log"

# Create data directory
mkdir -p "${HOME}/.claude-mem"

# Check if worker is already running
check_worker() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        if kill -0 "$PID" 2>/dev/null; then
            # Verify it's responding
            if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
                return 0
            fi
        fi
    fi
    return 1
}

# Start the worker
start_worker() {
    # Check if dotnet is available
    if ! command -v dotnet &> /dev/null; then
        echo "Error: .NET runtime not found. Please install .NET 9.0 SDK." >&2
        echo "Visit: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
        exit 1
    fi
    
    # Build if necessary
    if [ ! -f "${PLUGIN_ROOT}/src/ClaudeMem.Worker/bin/Release/net9.0/ClaudeMem.Worker.dll" ]; then
        echo "Building claude-mem-csharp..." >&2
        cd "${PLUGIN_ROOT}"
        dotnet build -c Release --nologo -v q >/dev/null 2>&1
    fi
    
    # Start worker in background
    cd "${PLUGIN_ROOT}"
    nohup dotnet run --project src/ClaudeMem.Worker -c Release --no-build \
        > "$LOG_FILE" 2>&1 &
    
    echo $! > "$PID_FILE"
    
    # Wait for worker to be ready (max 30 seconds)
    for i in {1..30}; do
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
