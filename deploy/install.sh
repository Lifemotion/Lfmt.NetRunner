#!/bin/bash
# Lfmt.NetRunner — one-command installation
# Usage: curl -sSL https://raw.githubusercontent.com/OWNER/Lfmt.NetRunner/main/deploy/install.sh | bash
set -euo pipefail

REPO="Lifemotion/Lfmt.NetRunner"
INSTALL_DIR="/opt/netrunner"
DATA_DIR="/var/lib/netrunner"
APPS_DIR="$DATA_DIR/apps"

echo "==> Installing Lfmt.NetRunner..."

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64) ASSET="netrunner-linux-x64.tar.gz" ;;
    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

# Get latest release URL
LATEST_URL=$(curl -sSL "https://api.github.com/repos/$REPO/releases/latest" \
    | grep "browser_download_url.*$ASSET" \
    | cut -d '"' -f 4)

if [ -z "$LATEST_URL" ]; then
    echo "ERROR: Could not find release asset $ASSET"
    exit 1
fi

echo "==> Downloading $LATEST_URL..."

# Create system user
if ! id netrunner &>/dev/null; then
    echo "==> Creating netrunner user..."
    sudo useradd -r -s /usr/sbin/nologin netrunner
fi

# Create directories
echo "==> Creating directories..."
sudo mkdir -p "$INSTALL_DIR" "$DATA_DIR" "$APPS_DIR"
sudo chown netrunner:netrunner "$DATA_DIR" "$APPS_DIR"
sudo chmod g+rwx "$APPS_DIR"

# Download and extract
TMP=$(mktemp -d)
curl -sSL "$LATEST_URL" -o "$TMP/netrunner.tar.gz"
sudo tar -xzf "$TMP/netrunner.tar.gz" -C "$INSTALL_DIR"
rm -rf "$TMP"
sudo chown -R netrunner:netrunner "$INSTALL_DIR"

# Install wrapper script
echo "==> Installing privileged wrapper script..."
sudo cp "$INSTALL_DIR/deploy/netrunner-sudo.sh" "$INSTALL_DIR/netrunner-sudo.sh" 2>/dev/null \
    || curl -sSL "https://raw.githubusercontent.com/$REPO/main/deploy/netrunner-sudo.sh" \
       | sudo tee "$INSTALL_DIR/netrunner-sudo.sh" > /dev/null
sudo chown root:root "$INSTALL_DIR/netrunner-sudo.sh"
sudo chmod 755 "$INSTALL_DIR/netrunner-sudo.sh"

# Configure sudoers
echo "==> Configuring sudoers..."
echo "netrunner ALL=(root) NOPASSWD: $INSTALL_DIR/netrunner-sudo.sh *" \
    | sudo tee /etc/sudoers.d/netrunner > /dev/null
sudo chmod 440 /etc/sudoers.d/netrunner

# Create config if not exists
if [ ! -f "$DATA_DIR/netrunner.conf" ]; then
    echo "==> Creating default configuration..."
    WEBHOOK_SECRET=$(openssl rand -hex 32)
    sudo tee "$DATA_DIR/netrunner.conf" > /dev/null << EOF
[server]
listen = 127.0.0.1:5050

[webhook]
secret = $WEBHOOK_SECRET

[paths]
apps_root = $APPS_DIR
dotnet_path = /usr/bin/dotnet
sudo_script = $INSTALL_DIR/netrunner-sudo.sh

[webhooks]
# clone_url = app-name
EOF
    sudo chown netrunner:netrunner "$DATA_DIR/netrunner.conf"
    sudo chmod 600 "$DATA_DIR/netrunner.conf"
fi

# Install systemd unit
echo "==> Installing systemd service..."
sudo tee /etc/systemd/system/netrunner.service > /dev/null << EOF
[Unit]
Description=Lfmt.NetRunner deployment service
After=network.target

[Service]
WorkingDirectory=$INSTALL_DIR
ExecStart=/usr/bin/dotnet $INSTALL_DIR/Lfmt.NetRunner.dll
Restart=always
RestartSec=5
SyslogIdentifier=netrunner
User=netrunner
Group=netrunner
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
ProtectSystem=strict
ReadWritePaths=$DATA_DIR
PrivateTmp=true
ProtectHome=true
NoNewPrivileges=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
PrivateDevices=true

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable netrunner
sudo systemctl restart netrunner

echo ""
echo "==> Lfmt.NetRunner installed successfully!"
echo "    Listening on: http://127.0.0.1:5050"
echo "    Webhook secret: $WEBHOOK_SECRET"
echo "    Config: $DATA_DIR/netrunner.conf"
echo "    Logs: journalctl -u netrunner -f"
echo ""
echo "    To access from outside, configure a reverse proxy or Teleport."
