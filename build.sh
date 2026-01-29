#!/usr/bin/env bash
# Build script for claude-mem-csharp
# Produces self-contained executables for Windows, Linux, and macOS

set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="$REPO_ROOT/bin"
SRC_DIR="$REPO_ROOT/src"

# Default to current platform
RUNTIME="${1:-$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)}"
CLEAN="${2:-false}"

# Map uname to .NET RID
case "$RUNTIME" in
    "darwin-arm64" | "osx-arm64")
        RUNTIME="osx-arm64"
        ;;
    "darwin-x86_64" | "osx-x64")
        RUNTIME="osx-x64"
        ;;
    "linux-x86_64" | "linux-x64")
        RUNTIME="linux-x64"
        ;;
    "linux-aarch64" | "linux-arm64")
        RUNTIME="linux-arm64"
        ;;
    "all")
        RUNTIMES=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
        ;;
    *)
        RUNTIMES=("$RUNTIME")
        ;;
esac

if [ -z "$RUNTIMES" ]; then
    RUNTIMES=("$RUNTIME")
fi

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

step() {
    echo -e "\n${CYAN}=> $1${NC}"
}

# Clean output directory
if [ "$CLEAN" = "true" ] || [ ! -d "$OUTPUT_DIR" ]; then
    step "Cleaning output directory..."
    rm -rf "$OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"
fi

# Build for each runtime
for rid in "${RUNTIMES[@]}"; do
    step "Building for $rid..."

    RID_OUTPUT_DIR="$OUTPUT_DIR/$rid"
    mkdir -p "$RID_OUTPUT_DIR"

    # Build ClaudeMem.Hooks (CLI)
    echo "  Building ClaudeMem.Hooks..."
    dotnet publish "$SRC_DIR/ClaudeMem.Hooks/ClaudeMem.Hooks.csproj" \
        -c Release \
        -r "$rid" \
        -o "$RID_OUTPUT_DIR" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:EnableCompressionInSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true

    # Build ClaudeMem.Mcp
    echo "  Building ClaudeMem.Mcp..."
    dotnet publish "$SRC_DIR/ClaudeMem.Mcp/ClaudeMem.Mcp.csproj" \
        -c Release \
        -r "$rid" \
        -o "$RID_OUTPUT_DIR" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:EnableCompressionInSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true

    # Build ClaudeMem.Worker
    echo "  Building ClaudeMem.Worker..."
    dotnet publish "$SRC_DIR/ClaudeMem.Worker/ClaudeMem.Worker.csproj" \
        -c Release \
        -r "$rid" \
        -o "$RID_OUTPUT_DIR" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:EnableCompressionInSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true

    # Clean up unnecessary files
    rm -f "$RID_OUTPUT_DIR"/*.pdb
    rm -f "$RID_OUTPUT_DIR"/*.deps.json
    rm -f "$RID_OUTPUT_DIR"/*.runtimeconfig.json

    echo -e "  ${GREEN}Build complete for $rid${NC}"
done

step "Build complete!"
echo ""
echo "Output directories:"
for rid in "${RUNTIMES[@]}"; do
    echo "  $rid: $OUTPUT_DIR/$rid"
done
