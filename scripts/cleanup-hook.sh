#!/bin/bash
# SessionEnd Hook - Mark session as completed
# Called when Claude Code session closes

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Read JSON input from stdin
INPUT=$(cat)

# Extract fields
SESSION_ID=$(echo "$INPUT" | jq -r '.session_id')
REASON=$(echo "$INPUT" | jq -r '.reason // "exit"')

# Skip if no session
if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
    echo '{"continue": true, "suppressOutput": true}'
    exit 0
fi

# Fire-and-forget HTTP POST to worker (2s timeout)
# Note: We don't wait for this - the session is closing anyway
curl -s --max-time 2 -X POST \
    "${WORKER_URL}/api/sessions/complete" \
    -H "Content-Type: application/json" \
    -d "$(jq -n \
        --arg sid "$SESSION_ID" \
        --arg reason "$REASON" \
        '{
            contentSessionId: $sid,
            reason: $reason
        }')" \
    >/dev/null 2>&1 &

echo '{"continue": true, "suppressOutput": true}'
