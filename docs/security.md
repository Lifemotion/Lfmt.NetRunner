# Security Model

## Application Isolation

### Dedicated System User

Each managed application runs under its own system user:

```
netrunner-{app-name}    # created when the app is registered
```

Creation:
```bash
sudo useradd -r -s /usr/sbin/nologin netrunner-{name}
```

Deletion (when the app is removed):
```bash
sudo userdel netrunner-{name}
```

This ensures:
- An app cannot read files belonging to other apps
- `/proc/{pid}/environ` is inaccessible to other users
- A process cannot send signals to other apps' processes

### File Ownership

```
/var/lib/netrunner/apps/{name}/
    config.ini          # netrunner-{name}:netrunner-{name}, 644
    current             # symlink
    releases/           # netrunner-{name}:netrunner-{name}, 755
    env                 # root:root, 600
    app.service         # root:root, 644
    deploy.log          # netrunner:netrunner, 644 (written by NetRunner, not the app)
```

Key point: `env` is owned by root. systemd (PID 1) reads `EnvironmentFile` before switching to the service user. The app receives variables in its environment but cannot read other apps' env files.

### Systemd Hardening

Every generated .service file includes:

```ini
# Filesystem
ProtectSystem=strict                # FS read-only
ReadWritePaths=/var/lib/netrunner/apps/{name}   # only its own directory
PrivateTmp=true                     # isolated /tmp
ProtectHome=true                    # /home, /root hidden

# Privileges
NoNewPrivileges=true                # no suid, capabilities
ProtectKernelTunables=true          # /proc/sys, /sys read-only
ProtectKernelModules=true           # no module loading
ProtectControlGroups=true           # cgroups read-only
PrivateDevices=true                 # no device node creation

# Resources
MemoryMax={memory}                  # RAM limit
CPUQuota={cpu}                      # CPU limit
```

## Access Control: sudoers

NetRunner runs as the `netrunner` user. It needs sudo for systemd management, scoped to specific commands:

```
# /etc/sudoers.d/netrunner

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
```

The `netrunner-` prefix is the key element: NetRunner cannot manage arbitrary system services.

## Network Security

### Listen Address Restriction

NetRunner listens only on the IP:port specified in `netrunner.conf`:

```ini
[server]
listen = 127.0.0.1:5050
```

Default is `127.0.0.1` (localhost only). External access goes through Teleport proxy, which handles authentication.

### Forgejo Webhook: HMAC-SHA256

Forgejo signs the webhook body. NetRunner verifies the signature:

1. Extracts `X-Forgejo-Signature` from headers (hex string)
2. Computes HMAC-SHA256 of the request body with the shared secret
3. Compares using `CryptographicOperations.FixedTimeEquals` (timing attack protection)
4. On mismatch — 403 Forbidden

The secret is configured in `netrunner.conf`:

```ini
[webhook]
secret = <shared-secret>
```
