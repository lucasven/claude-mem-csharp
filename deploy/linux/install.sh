#!/bin/bash
# Claude-Mem Worker Installation Script for Linux

set -e

INSTALL_PATH="/opt/claude-mem"
SERVICE_NAME="claude-mem-worker"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Claude-Mem Worker Installation"
echo "=============================="

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo ./install.sh"
    exit 1
fi

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "x86_64" ]; then
    RID="linux-x64"
elif [ "$ARCH" = "aarch64" ]; then
    RID="linux-arm64"
else
    echo "Unsupported architecture: $ARCH"
    exit 1
fi
echo "Detected architecture: ${ARCH} (${RID})"

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

# Install service file
echo "Installing systemd service..."
cp "${SCRIPT_DIR}/claude-mem-worker.service" /etc/systemd/system/

# Reload systemd
systemctl daemon-reload

# Enable and start
echo "Enabling and starting service..."
systemctl enable ${SERVICE_NAME}
systemctl restart ${SERVICE_NAME}

# Wait and verify
sleep 2

if curl -s http://127.0.0.1:37777/health | grep -q "healthy"; then
    echo ""
    echo "✅ Installation successful!"
    echo ""
    echo "Service is running on: http://127.0.0.1:37777"
    echo ""
    echo "Useful commands:"
    echo "  systemctl status ${SERVICE_NAME}"
    echo "  journalctl -u ${SERVICE_NAME} -f"
    echo ""
    echo "To uninstall:"
    echo "  systemctl stop ${SERVICE_NAME}"
    echo "  systemctl disable ${SERVICE_NAME}"
    echo "  rm /etc/systemd/system/${SERVICE_NAME}.service"
else
    echo ""
    echo "⚠️  Service may not have started correctly"
    echo "Check status: systemctl status ${SERVICE_NAME}"
    echo "Check logs: journalctl -u ${SERVICE_NAME}"
fi
