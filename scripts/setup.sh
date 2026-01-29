#!/usr/bin/env bash
# Plugin setup script - downloads binaries for the current platform
# Called automatically when plugin is installed via Claude Code

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PLUGIN_ROOT="${1:-$(dirname "$SCRIPT_DIR")}"
BIN_DIR="$PLUGIN_ROOT/bin"

REPO_OWNER="lucasven"
REPO_NAME="claude-mem-csharp"

CYAN='\033[0;36m'
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

step() {
    echo -e "${CYAN}=> $1${NC}"
}

# Check if binaries already exist
if [ -f "$BIN_DIR/claude-mem-csharp" ] && \
   [ -f "$BIN_DIR/ClaudeMem.Mcp" ] && \
   [ -f "$BIN_DIR/ClaudeMem.Worker" ]; then
    echo "Binaries already installed."
    exit 0
fi

# Detect platform
get_platform() {
    local os arch

    os="$(uname -s | tr '[:upper:]' '[:lower:]')"
    arch="$(uname -m)"

    case "$os" in
        "darwin") os="osx" ;;
        "linux") os="linux" ;;
        *) echo "Unsupported OS: $os" >&2; exit 1 ;;
    esac

    case "$arch" in
        "x86_64" | "amd64") arch="x64" ;;
        "arm64" | "aarch64") arch="arm64" ;;
        *) echo "Unsupported architecture: $arch" >&2; exit 1 ;;
    esac

    echo "${os}-${arch}"
}

RID=$(get_platform)
step "Detected platform: $RID"

# Get latest release
step "Fetching latest release..."
API_URL="https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/releases/latest"
RELEASE_INFO=$(curl -s "$API_URL")

VERSION=$(echo "$RELEASE_INFO" | grep '"tag_name"' | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')
ASSET_NAME="claude-mem-csharp-$RID.tar.gz"
DOWNLOAD_URL=$(echo "$RELEASE_INFO" | grep "browser_download_url" | grep "$ASSET_NAME" | sed -E 's/.*"browser_download_url": *"([^"]+)".*/\1/')

if [ -z "$DOWNLOAD_URL" ]; then
    echo -e "${RED}Error: Could not find release asset: $ASSET_NAME${NC}" >&2
    exit 1
fi

# Download
step "Downloading $ASSET_NAME..."
TEMP_FILE=$(mktemp)
curl -fsSL "$DOWNLOAD_URL" -o "$TEMP_FILE"

# Extract to bin directory
step "Installing to $BIN_DIR..."
mkdir -p "$BIN_DIR"
tar -xzf "$TEMP_FILE" -C "$BIN_DIR" --strip-components=1 || tar -xzf "$TEMP_FILE" -C "$BIN_DIR"
rm -f "$TEMP_FILE"

# Make executables
chmod +x "$BIN_DIR/claude-mem-csharp" 2>/dev/null || true
chmod +x "$BIN_DIR/ClaudeMem.Mcp" 2>/dev/null || true
chmod +x "$BIN_DIR/ClaudeMem.Worker" 2>/dev/null || true

step "Setup complete! Version: $VERSION"
