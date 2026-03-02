#!/bin/bash
# UserPromptSubmit Hook - Initialize session and save user prompt
# Called when user submits any prompt

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Read JSON input from stdin
INPUT=$(cat)

# Extract fields
SESSION_ID=$(echo "$INPUT" | jq -r '.session_id')
CWD=$(echo "$INPUT" | jq -r '.cwd // "."')
PROMPT=$(echo "$INPUT" | jq -r '.prompt // ""')
PROJECT=$(basename "$CWD")

# Skip if no prompt or session
if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
    echo '{"continue": true, "suppressOutput": true}'
    exit 0
fi

# Ensure worker is running
source "${PLUGIN_ROOT}/scripts/ensure-worker.sh" >/dev/null 2>&1

# Initialize session via HTTP
curl -s --max-time 5 -X POST \
    "${WORKER_URL}/api/sessions/init" \
    -H "Content-Type: application/json" \
    -d "$(jq -n \
        --arg sid "$SESSION_ID" \
        --arg proj "$PROJECT" \
        --arg prompt "$PROMPT" \
        '{
            contentSessionId: $sid,
            project: $proj,
            prompt: $prompt
        }')" \
    >/dev/null 2>&1 || true

echo '{"continue": true, "suppressOutput": true}'
