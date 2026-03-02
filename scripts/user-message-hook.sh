#!/bin/bash
# SessionStart Hook - Display user messages
# Shows helpful info about claude-mem status

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Check if this is first run (no build yet)
if [ ! -f "${PLUGIN_ROOT}/src/ClaudeMem.Worker/bin/Release/net9.0/ClaudeMem.Worker.dll" ]; then
    echo "🧠 claude-mem-csharp: First run - building..." >&2
fi

# Check worker status
if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
    # Get stats
    STATS=$(curl -s --max-time 2 "${WORKER_URL}/api/stats" 2>/dev/null || echo "{}")
    SESSIONS=$(echo "$STATS" | jq -r '.sessions // 0')
    OBSERVATIONS=$(echo "$STATS" | jq -r '.observations // 0')
    
    if [ "$OBSERVATIONS" != "0" ]; then
        echo "🧠 claude-mem: ${OBSERVATIONS} observations across ${SESSIONS} sessions" >&2
        echo "   View at: ${WORKER_URL}" >&2
    fi
fi

# Always continue
echo '{"continue": true, "suppressOutput": true}'
