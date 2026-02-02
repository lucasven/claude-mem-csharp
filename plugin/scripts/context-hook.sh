#!/bin/bash
# SessionStart Hook - Inject context from previous sessions
# Called when user opens Claude Code or resumes session

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Read JSON input from stdin
INPUT=$(cat)

# Extract fields
CWD=$(echo "$INPUT" | jq -r '.cwd // "."')
PROJECT=$(basename "$CWD")

# Ensure worker is running
source "${PLUGIN_ROOT}/scripts/ensure-worker.sh" >/dev/null 2>&1

# Fetch context from worker
CONTEXT=$(curl -s --max-time 10 \
    "${WORKER_URL}/api/context/inject?project=${PROJECT}" \
    2>/dev/null || echo "")

# Output context for Claude if we got any
if [ -n "$CONTEXT" ] && [ "$CONTEXT" != "null" ] && [ "$CONTEXT" != "{}" ]; then
    # Return as additionalContext
    jq -n --arg ctx "$CONTEXT" '{
        "hookSpecificOutput": {
            "hookEventName": "SessionStart",
            "additionalContext": $ctx
        }
    }'
else
    # No context to inject
    echo '{"continue": true}'
fi
