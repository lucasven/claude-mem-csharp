#!/bin/bash
# Ensure the claude-mem-csharp Worker is running
# Called by all hooks before making HTTP requests

set -e

PLUGIN_ROOT="${CLAUDE_PLUGIN_ROOT:-$(dirname "$(dirname "$0")")}"
WORKER_PORT="${CLAUDE_MEM_WORKER_PORT:-37777}"
WORKER_URL="http://127.0.0.1:${WORKER_PORT}"
PID_FILE="${HOME}/.claude-mem/worker.pid"
LOG_FILE="${HOME}/.claude-mem/worker.log"

# Create data directory
mkdir -p "${HOME}/.claude-mem"

# Check if worker is already running
check_worker() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        if kill -0 "$PID" 2>/dev/null; then
            # Verify it's responding
            if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
                return 0
            fi
        fi
    fi
    return 1
}

# Install .NET SDK
install_dotnet() {
    echo "Installing .NET 9.0 SDK..." >&2
    
    # Detect OS
    OS="$(uname -s)"
    
    case "$OS" in
        Darwin)
            # macOS - prefer Homebrew
            if command -v brew &> /dev/null; then
                echo "Using Homebrew..." >&2
                brew install dotnet@9 2>&1 | tail -3 >&2
                # Link if needed
                brew link --overwrite dotnet@9 2>/dev/null || true
            else
                # Use Microsoft install script
                echo "Using Microsoft install script..." >&2
                curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
                export PATH="$HOME/.dotnet:$PATH"
                echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$HOME/.bashrc"
                echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$HOME/.zshrc" 2>/dev/null || true
            fi
            ;;
        Linux)
            # Linux - try package manager first, then Microsoft script
            if command -v apt-get &> /dev/null; then
                # Ubuntu/Debian
                echo "Using apt..." >&2
                # Add Microsoft repo
                curl -sSL https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb 2>/dev/null
                if [ -f /tmp/packages-microsoft-prod.deb ]; then
                    sudo dpkg -i /tmp/packages-microsoft-prod.deb 2>/dev/null || true
                    sudo apt-get update -qq
                    sudo apt-get install -y -qq dotnet-sdk-9.0 2>&1 | tail -3 >&2
                else
                    # Fallback to install script
                    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
                    export PATH="$HOME/.dotnet:$PATH"
                    echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$HOME/.bashrc"
                fi
            elif command -v dnf &> /dev/null; then
                # Fedora/RHEL
                echo "Using dnf..." >&2
                sudo dnf install -y dotnet-sdk-9.0 2>&1 | tail -3 >&2
            elif command -v pacman &> /dev/null; then
                # Arch
                echo "Using pacman..." >&2
                sudo pacman -S --noconfirm dotnet-sdk 2>&1 | tail -3 >&2
            else
                # Fallback to Microsoft install script
                echo "Using Microsoft install script..." >&2
                curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
                export PATH="$HOME/.dotnet:$PATH"
                echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$HOME/.bashrc"
            fi
            ;;
        MINGW*|MSYS*|CYGWIN*)
            # Windows (Git Bash/MSYS2)
            if command -v winget &> /dev/null; then
                echo "Using winget..." >&2
                winget install Microsoft.DotNet.SDK.9 --silent 2>&1 | tail -3 >&2
            else
                echo "Error: Please install .NET 9.0 SDK manually on Windows." >&2
                echo "Visit: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
                exit 1
            fi
            ;;
        *)
            echo "Error: Unsupported OS: $OS" >&2
            echo "Please install .NET 9.0 SDK manually." >&2
            echo "Visit: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
            exit 1
            ;;
    esac
    
    # Verify installation
    if command -v dotnet &> /dev/null; then
        echo ".NET $(dotnet --version) installed successfully!" >&2
        return 0
    elif [ -x "$HOME/.dotnet/dotnet" ]; then
        export PATH="$HOME/.dotnet:$PATH"
        echo ".NET $(dotnet --version) installed successfully!" >&2
        return 0
    else
        echo "Error: .NET installation failed." >&2
        exit 1
    fi
}

# Start the worker
start_worker() {
    # Check if dotnet is available, install if not
    if ! command -v dotnet &> /dev/null; then
        # Check ~/.dotnet too (user install location)
        if [ -x "$HOME/.dotnet/dotnet" ]; then
            export PATH="$HOME/.dotnet:$PATH"
        else
            install_dotnet
        fi
    fi
    
    # Verify version (need 9.0+)
    DOTNET_VERSION=$(dotnet --version 2>/dev/null | cut -d. -f1)
    if [ "$DOTNET_VERSION" -lt 9 ] 2>/dev/null; then
        echo "Warning: .NET $DOTNET_VERSION detected, upgrading to 9.0..." >&2
        install_dotnet
    fi
    
    # Build if necessary
    if [ ! -f "${PLUGIN_ROOT}/src/ClaudeMem.Worker/bin/Release/net9.0/ClaudeMem.Worker.dll" ]; then
        echo "Building claude-mem-csharp..." >&2
        cd "${PLUGIN_ROOT}"
        dotnet build -c Release --nologo -v q >/dev/null 2>&1
    fi
    
    # Start worker in background
    cd "${PLUGIN_ROOT}"
    nohup dotnet run --project src/ClaudeMem.Worker -c Release --no-build \
        > "$LOG_FILE" 2>&1 &
    
    echo $! > "$PID_FILE"
    
    # Wait for worker to be ready (max 30 seconds)
    for i in {1..30}; do
        if curl -s --max-time 2 "${WORKER_URL}/health" >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done
    
    echo "Error: Worker failed to start. Check ${LOG_FILE}" >&2
    exit 1
}

# Main
if ! check_worker; then
    start_worker
fi

echo "${WORKER_URL}"
