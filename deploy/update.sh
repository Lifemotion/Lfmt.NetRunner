#!/bin/bash
# Lfmt.NetRunner — one-command update
# Usage: curl -sSL https://raw.githubusercontent.com/OWNER/Lfmt.NetRunner/main/deploy/update.sh | bash
set -euo pipefail

REPO="Lifemotion/Lfmt.NetRunner"
INSTALL_DIR="/opt/netrunner"
ASSET="netrunner-linux-x64.tar.gz"

# Get current version
CURRENT=""
if [ -f "$INSTALL_DIR/Lfmt.NetRunner.dll" ]; then
    CURRENT=$(dotnet "$INSTALL_DIR/Lfmt.NetRunner.dll" --version 2>/dev/null || echo "unknown")
fi

# Get latest release
echo "==> Checking for updates..."
RELEASE_INFO=$(curl -sSL "https://api.github.com/repos/$REPO/releases/latest")
LATEST_TAG=$(echo "$RELEASE_INFO" | grep '"tag_name"' | cut -d '"' -f 4)
LATEST_URL=$(echo "$RELEASE_INFO" | grep "browser_download_url.*$ASSET" | cut -d '"' -f 4)

if [ -z "$LATEST_URL" ]; then
    echo "ERROR: Could not find release asset"
    exit 1
fi

echo "    Current: ${CURRENT:-not installed}"
echo "    Latest:  $LATEST_TAG"

# Download
TMP=$(mktemp -d)
echo "==> Downloading $LATEST_TAG..."
curl -sSL "$LATEST_URL" -o "$TMP/netrunner.tar.gz"

# Stop service
echo "==> Stopping service..."
sudo systemctl stop netrunner

# Extract
echo "==> Updating files..."
sudo tar -xzf "$TMP/netrunner.tar.gz" -C "$INSTALL_DIR"
rm -rf "$TMP"
sudo chown -R netrunner:netrunner "$INSTALL_DIR"

# Update wrapper script
sudo cp "$INSTALL_DIR/deploy/netrunner-sudo.sh" "$INSTALL_DIR/netrunner-sudo.sh" 2>/dev/null || true
sudo chown root:root "$INSTALL_DIR/netrunner-sudo.sh" 2>/dev/null || true
sudo chmod 755 "$INSTALL_DIR/netrunner-sudo.sh" 2>/dev/null || true

# Restart
echo "==> Starting service..."
sudo systemctl daemon-reload
sudo systemctl start netrunner

echo ""
echo "==> Updated to $LATEST_TAG"
echo "    Status: $(sudo systemctl is-active netrunner)"
