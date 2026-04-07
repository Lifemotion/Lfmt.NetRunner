#!/bin/bash
# Install .NET 10 SDK on Debian/Ubuntu
# Usage: curl -sSL https://raw.githubusercontent.com/Lifemotion/Lfmt.NetRunner/main/deploy/install-dotnet.sh | bash
set -euo pipefail

if command -v dotnet &>/dev/null; then
    echo ".NET $(dotnet --version) is already installed"
    exit 0
fi

echo "==> Installing .NET SDK..."

sudo apt-get update -qq
sudo apt-get install -y -qq curl wget ca-certificates libicu-dev

wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
sudo /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
rm -f /tmp/dotnet-install.sh

echo "==> .NET $(dotnet --version) installed successfully"
