# Server Setup

Step-by-step guide for Debian 13+.

## 1. Install .NET SDK

The SDK (not just runtime) is required — NetRunner compiles applications on the server.

```bash
sudo apt update
sudo apt install -y dotnet-sdk-10.0
```

## 2. Create NetRunner User

```bash
sudo useradd -r -s /usr/sbin/nologin netrunner
```

## 3. Create Directories

```bash
sudo mkdir -p /var/lib/netrunner/apps
sudo mkdir -p /var/log/netrunner
sudo chown -R netrunner:netrunner /var/lib/netrunner /var/log/netrunner
```

## 4. Configure NetRunner

```bash
sudo tee /var/lib/netrunner/netrunner.conf > /dev/null << 'EOF'
[server]
listen = 127.0.0.1:5050

[webhook]
secret = <generate with: openssl rand -hex 32>

[paths]
apps_root = /var/lib/netrunner/apps
dotnet_path = /usr/bin/dotnet

[webhooks]
# clone_url = app-name
# https://git.example.com/user/app.git = my-app
EOF

sudo chown netrunner:netrunner /var/lib/netrunner/netrunner.conf
sudo chmod 600 /var/lib/netrunner/netrunner.conf
```

## 5. Configure sudoers

```bash
sudo tee /etc/sudoers.d/netrunner > /dev/null << 'EOF'
# Service management (netrunner-* prefix only)
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl start netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl stop netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl restart netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl status netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl enable netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl disable netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/bin/systemctl daemon-reload

# Service file installation
netrunner ALL=(root) NOPASSWD: /usr/bin/cp /var/lib/netrunner/apps/*/app.service /etc/systemd/system/netrunner-*.service

# Log access
netrunner ALL=(root) NOPASSWD: /usr/bin/journalctl -u netrunner-* *

# App user management
netrunner ALL=(root) NOPASSWD: /usr/sbin/useradd -r -s /usr/sbin/nologin netrunner-*
netrunner ALL=(root) NOPASSWD: /usr/sbin/userdel netrunner-*

# File ownership management
netrunner ALL=(root) NOPASSWD: /usr/bin/chown -R netrunner-*\:netrunner-* /var/lib/netrunner/apps/*
EOF

sudo chmod 440 /etc/sudoers.d/netrunner
```

## 6. Install NetRunner

### Build (on development machine)

```bash
dotnet publish Lfmt.NetRunner -c Release -o ./publish
```

### Create application directory on server

```bash
sudo mkdir -p /opt/netrunner
sudo chown netrunner:netrunner /opt/netrunner
```

### Copy to server

```bash
scp -i ~/.ssh/id_proxmox -r ./publish/* user@server:/opt/netrunner/
```

### Install systemd unit

```bash
sudo cp deploy/netrunner.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable netrunner
sudo systemctl start netrunner
```

## 7. Verify

```bash
# Service status
sudo systemctl status netrunner

# Logs
sudo journalctl -u netrunner -f

# Availability (if listening on localhost)
curl http://127.0.0.1:5050/
```

## 8. Teleport Configuration (optional)

If NetRunner is behind Teleport proxy, configure app access:

```yaml
# /etc/teleport.yaml (fragment)
app_service:
  enabled: true
  apps:
    - name: netrunner
      uri: http://127.0.0.1:5050
```

## 9. Forgejo Configuration (optional)

In the Forgejo repository: Settings → Webhooks → Add Webhook:

- **Target URL:** `https://netrunner.example.com/api/webhook/forgejo`
- **Secret:** same as in `netrunner.conf` [webhook] secret
- **Trigger:** Push events
- **Branch filter:** `main`
