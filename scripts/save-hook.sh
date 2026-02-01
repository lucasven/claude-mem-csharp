#!/bin/bash
# PostToolUse Hook - Save tool observations
# Called after Claude uses any tool

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"

# Tools to skip (low-value or infrastructure noise)
SKIP_TOOLS="ListMcpResourcesTool|SlashCommand|Skill|TodoWrite|AskUserQuestion|mcp__"

# Read JSON input from stdin
INPUT=$(cat)

# Extract fields
SESSION_ID=$(echo "$INPUT" | jq -r '.session_id')
CWD=$(echo "$INPUT" | jq -r '.cwd // "."')
TOOL_NAME=$(echo "$INPUT" | jq -r '.tool_name // ""')
TOOL_INPUT=$(echo "$INPUT" | jq -c '.tool_input // {}')
TOOL_RESPONSE=$(echo "$INPUT" | jq -r '.tool_response // ""')
PROJECT=$(basename "$CWD")

# Skip if no session or tool name
if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ] || [ -z "$TOOL_NAME" ]; then
    echo '{"continue": true, "suppressOutput": true}'
    exit 0
fi

# Skip blocklisted tools
if echo "$TOOL_NAME" | grep -qE "$SKIP_TOOLS"; then
    echo '{"continue": true, "suppressOutput": true}'
    exit 0
fi

# Determine observation type based on tool
case "$TOOL_NAME" in
    Read|Grep|Glob|Search)
        OBS_TYPE="discovery"
        ;;
    Write|Edit|apply_patch)
        OBS_TYPE="modification"
        ;;
    Bash|exec|process)
        OBS_TYPE="action"
        ;;
    *)
        OBS_TYPE="observation"
        ;;
esac

# Generate title from tool name and input
TITLE="${TOOL_NAME}"
if [ "$TOOL_NAME" = "Read" ]; then
    FILE=$(echo "$TOOL_INPUT" | jq -r '.file_path // .path // ""' | head -c 50)
    [ -n "$FILE" ] && TITLE="Read: $FILE"
elif [ "$TOOL_NAME" = "Write" ]; then
    FILE=$(echo "$TOOL_INPUT" | jq -r '.file_path // .path // ""' | head -c 50)
    [ -n "$FILE" ] && TITLE="Write: $FILE"
elif [ "$TOOL_NAME" = "Bash" ]; then
    CMD=$(echo "$TOOL_INPUT" | jq -r '.command // ""' | head -c 50)
    [ -n "$CMD" ] && TITLE="Bash: $CMD"
fi

# Truncate response if too long (keep first 10KB)
TOOL_RESPONSE=$(echo "$TOOL_RESPONSE" | head -c 10240)

# Fire-and-forget HTTP POST to worker (2s timeout)
curl -s --max-time 2 -X POST \
    "${WORKER_URL}/api/sessions/observations" \
    -H "Content-Type: application/json" \
    -d "$(jq -n \
        --arg sid "$SESSION_ID" \
        --arg tool "$TOOL_NAME" \
        --argjson input "$TOOL_INPUT" \
        --arg response "$TOOL_RESPONSE" \
        --arg title "$TITLE" \
        --arg type "$OBS_TYPE" \
        '{
            contentSessionId: $sid,
            toolName: $tool,
            toolInput: ($input | tostring),
            toolResponse: $response,
            title: $title,
            observationType: $type
        }')" \
    >/dev/null 2>&1 &

echo '{"continue": true, "suppressOutput": true}'
