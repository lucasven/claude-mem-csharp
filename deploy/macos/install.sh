#!/bin/bash
# Claude-Mem Worker Installation Script for macOS

set -e

INSTALL_PATH="${HOME}/Applications/claude-mem"
PLIST_NAME="com.claudemem.worker.plist"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Claude-Mem Worker Installation"
echo "=============================="

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
    echo "Detected: Apple Silicon (M1/M2/M3)"
else
    RID="osx-x64"
    echo "Detected: Intel Mac"
fi

# Check if already built
if [ ! -f "${INSTALL_PATH}/ClaudeMem.Worker" ]; then
    echo ""
    echo "Executable not found at: ${INSTALL_PATH}/ClaudeMem.Worker"
    echo ""
    echo "Please build first from the project root:"
    echo "  dotnet publish src/ClaudeMem.Worker -c Release -r ${RID} --self-contained -o ${INSTALL_PATH}"
    echo ""
    exit 1
fi

# Make executable
chmod +x "${INSTALL_PATH}/ClaudeMem.Worker"

# Create plist with correct user path
PLIST_DEST="${HOME}/Library/LaunchAgents/${PLIST_NAME}"
echo "Installing launch agent to: ${PLIST_DEST}"

# Replace USER placeholder with actual username
sed "s|/Users/USER|${HOME}|g" "${SCRIPT_DIR}/${PLIST_NAME}" > "${PLIST_DEST}"

# Unload if already loaded
launchctl unload "${PLIST_DEST}" 2>/dev/null || true

# Load the agent
echo "Loading launch agent..."
launchctl load "${PLIST_DEST}"

# Wait and verify
sleep 2

if curl -s http://127.0.0.1:37777/health | grep -q "healthy"; then
    echo ""
    echo "✅ Installation successful!"
    echo ""
    echo "Service is running on: http://127.0.0.1:37777"
    echo "Logs: tail -f /tmp/claude-mem-worker.log"
    echo ""
    echo "To uninstall:"
    echo "  launchctl unload ${PLIST_DEST}"
    echo "  rm ${PLIST_DEST}"
else
    echo ""
    echo "⚠️  Service may not have started correctly"
    echo "Check logs: cat /tmp/claude-mem-worker.log"
fi
