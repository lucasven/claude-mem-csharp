#!/bin/bash
# Stop Hook - Generate session summary
# Called when user stops asking questions

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Read JSON input from stdin
INPUT=$(cat)

# Extract fields
SESSION_ID=$(echo "$INPUT" | jq -r '.session_id')
CWD=$(echo "$INPUT" | jq -r '.cwd // "."')
TRANSCRIPT_PATH=$(echo "$INPUT" | jq -r '.transcript_path // ""')

# Skip if no session
if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
    echo '{"continue": true, "suppressOutput": true}'
    exit 0
fi

# Extract last messages from transcript if available
LAST_USER_MSG=""
LAST_ASSISTANT_MSG=""

if [ -n "$TRANSCRIPT_PATH" ] && [ -f "$TRANSCRIPT_PATH" ]; then
    # Get last user message
    LAST_USER_MSG=$(grep -o '{"role":"user"[^}]*}' "$TRANSCRIPT_PATH" 2>/dev/null | tail -1 | jq -r '.content // ""' 2>/dev/null || echo "")
    
    # Get last assistant message
    LAST_ASSISTANT_MSG=$(grep -o '{"role":"assistant"[^}]*}' "$TRANSCRIPT_PATH" 2>/dev/null | tail -1 | jq -r '.content // ""' 2>/dev/null || echo "")
fi

# Fire-and-forget HTTP POST to worker (2s timeout)
curl -s --max-time 2 -X POST \
    "${WORKER_URL}/api/sessions/summarize" \
    -H "Content-Type: application/json" \
    -d "$(jq -n \
        --arg sid "$SESSION_ID" \
        --arg last_user "$LAST_USER_MSG" \
        --arg last_assistant "$LAST_ASSISTANT_MSG" \
        '{
            contentSessionId: $sid,
            lastUserMessage: $last_user,
            lastAssistantMessage: $last_assistant
        }')" \
    >/dev/null 2>&1 &

echo '{"continue": true, "suppressOutput": true}'
