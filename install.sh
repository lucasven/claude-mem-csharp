#!/usr/bin/env bash
# ClaudeMem Installation Script for macOS/Linux
# Downloads pre-built self-contained binaries - no .NET SDK required
# Usage: curl -fsSL https://raw.githubusercontent.com/lucasven/claude-mem-csharp/main/install.sh | bash

set -e

REPO_OWNER="lucasven"
REPO_NAME="claude-mem-csharp"
INSTALL_DIR="$HOME/.claude-mem-csharp"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

step() {
    echo -e "\n${CYAN}=> $1${NC}"
}

error() {
    echo -e "${RED}Error: $1${NC}" >&2
    exit 1
}

# Detect platform
get_platform() {
    local os arch

    os="$(uname -s | tr '[:upper:]' '[:lower:]')"
    arch="$(uname -m)"

    case "$os" in
        "darwin")
            os="osx"
            ;;
        "linux")
            os="linux"
            ;;
        *)
            error "Unsupported operating system: $os"
            ;;
    esac

    case "$arch" in
        "x86_64" | "amd64")
            arch="x64"
            ;;
        "arm64" | "aarch64")
            arch="arm64"
            ;;
        *)
            error "Unsupported architecture: $arch"
            ;;
    esac

    echo "${os}-${arch}"
}

# Get latest release info
get_latest_release() {
    local api_url="https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/releases/latest"

    if command -v curl &> /dev/null; then
        curl -s "$api_url"
    elif command -v wget &> /dev/null; then
        wget -qO- "$api_url"
    else
        error "Neither curl nor wget found. Please install one of them."
    fi
}

# Download file
download_file() {
    local url="$1"
    local output="$2"

    if command -v curl &> /dev/null; then
        curl -fsSL "$url" -o "$output"
    elif command -v wget &> /dev/null; then
        wget -q "$url" -O "$output"
    fi
}

# Detect platform
RID=$(get_platform)
step "Detected platform: $RID"

# Get latest release
step "Fetching latest release..."
RELEASE_INFO=$(get_latest_release)

VERSION=$(echo "$RELEASE_INFO" | grep '"tag_name"' | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')
echo "Latest version: $VERSION"

# Find download URL
ASSET_NAME="claude-mem-csharp-$RID.tar.gz"
DOWNLOAD_URL=$(echo "$RELEASE_INFO" | grep "browser_download_url" | grep "$ASSET_NAME" | sed -E 's/.*"browser_download_url": *"([^"]+)".*/\1/')

if [ -z "$DOWNLOAD_URL" ]; then
    error "Could not find release asset: $ASSET_NAME"
fi

echo "Download URL: $DOWNLOAD_URL"

# Download
step "Downloading $ASSET_NAME..."
TEMP_FILE=$(mktemp)
download_file "$DOWNLOAD_URL" "$TEMP_FILE" || error "Failed to download release"

# Create installation directory
step "Installing to $INSTALL_DIR..."

if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
fi
mkdir -p "$INSTALL_DIR"

# Extract
tar -xzf "$TEMP_FILE" -C "$INSTALL_DIR" --strip-components=1 || {
    # Try without strip-components if there's no nested directory
    tar -xzf "$TEMP_FILE" -C "$INSTALL_DIR"
}
rm -f "$TEMP_FILE"

# Make executables
chmod +x "$INSTALL_DIR/claude-mem-csharp" 2>/dev/null || true
chmod +x "$INSTALL_DIR/ClaudeMem.Mcp" 2>/dev/null || true
chmod +x "$INSTALL_DIR/ClaudeMem.Worker" 2>/dev/null || true

# Verify installation
if [ ! -f "$INSTALL_DIR/claude-mem-csharp" ]; then
    error "Installation failed - executable not found"
fi

step "Installation complete!"

echo ""
echo -e "${GREEN}ClaudeMem has been installed to: $INSTALL_DIR${NC}"
echo "Version: $VERSION"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo ""
echo -e "${CYAN}Option 1: Install as Claude Code plugin (recommended)${NC}"
echo "Run these commands in Claude Code:"
echo "  /plugin marketplace add $REPO_OWNER/$REPO_NAME"
echo "  /plugin install claude-mem-csharp@claude-mem-csharp-marketplace"
echo ""
echo -e "${CYAN}Option 2: Manual configuration${NC}"
echo "  1. Start the worker service:"
echo "     $INSTALL_DIR/claude-mem-csharp worker start"
echo ""
echo "  2. Check the status:"
echo "     $INSTALL_DIR/claude-mem-csharp status"
echo ""
echo "  3. Add to your PATH for easier access (optional):"
echo "     echo 'export PATH=\"\$PATH:$INSTALL_DIR\"' >> ~/.bashrc"
echo "     source ~/.bashrc"
echo ""
