# Security Model

## Application Isolation

### Dedicated System User

Each managed application runs under its own system user:

```
netrunner-{app-name}    # created when the app is registered
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

Key point: `env` is owned by `root:root` with mode 600. systemd (PID 1) reads `EnvironmentFile` as root before switching to the service user. The app receives variables in its environment but cannot read the file directly, nor other apps' env files.

NetRunner (running as `netrunner`) writes to `env` via the privileged wrapper script (see below), which validates the target path and ensures ownership remains `root:root`.

### Systemd Hardening

**NetRunner itself** runs with minimal hardening (`PrivateTmp`, `PrivateDevices` only). It cannot use `NoNewPrivileges`, `ProtectSystem`, or `ProtectHome` because it needs sudo for user creation, service management, and `dotnet publish`. `DOTNET_CLI_HOME` and `HOME` are set to `/var/lib/netrunner` since the `netrunner` user has no home directory.

**Managed applications** get full hardening. Every generated .service file includes:

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

### Custom Service File Restrictions

When a custom .service file is provided via `custom_file` in `.netrunner`, NetRunner **always force-overrides** the following directives from its own template, regardless of what the custom file contains:

- `User=netrunner-{name}` — forced to the app's dedicated user
- `Group=netrunner-{name}` — forced to the app's dedicated group
- All hardening directives (`ProtectSystem`, `NoNewPrivileges`, etc.)
- `EnvironmentFile` — forced to the app's env file path

This prevents privilege escalation via a malicious custom service file that sets `User=root` or adds dangerous `ExecStartPre`/`ExecStartPost` commands.

Allowed customizations in custom files: `WorkingDirectory`, `Environment`, `ReadWritePaths`, `ExecStart` arguments.

## Access Control: Privileged Wrapper Script

Direct sudo commands with wildcard paths are vulnerable to path traversal (`*` in sudoers matches `/`). Instead, all privileged operations go through a single wrapper script with strict input validation.

### Wrapper script: `/opt/netrunner/netrunner-sudo.sh`

```bash
#!/bin/bash
set -euo pipefail

APPS_ROOT="/var/lib/netrunner/apps"

# Validate app name: only [a-z0-9-], 2-48 chars, no leading/trailing hyphen
validate_name() {
    if [[ ! "$1" =~ ^[a-z0-9][a-z0-9-]{0,46}[a-z0-9]$ ]]; then
        echo "Invalid app name: $1" >&2
        exit 1
    fi
}

case "${1:-}" in
    # --- systemctl commands ---
    start|stop|restart|enable|disable)
        validate_name "$2"
        systemctl "$1" "netrunner-$2"
        ;;
    status)
        validate_name "$2"
        systemctl status "netrunner-$2" || true
        ;;
    daemon-reload)
        systemctl daemon-reload
        ;;

    # --- Service file installation ---
    install-service)
        validate_name "$2"
        cp "$APPS_ROOT/$2/app.service" "/etc/systemd/system/netrunner-$2.service"
        ;;

    # --- Journal logs ---
    logs)
        validate_name "$2"
        lines="${3:-100}"
        # Validate lines is a number
        if [[ ! "$lines" =~ ^[0-9]+$ ]]; then
            echo "Invalid line count: $lines" >&2
            exit 1
        fi
        journalctl -u "netrunner-$2" -n "$lines" --no-pager
        ;;

    # --- User management ---
    create-user)
        validate_name "$2"
        useradd -r -s /usr/sbin/nologin "netrunner-$2"
        ;;
    delete-user)
        validate_name "$2"
        userdel "netrunner-$2" || true
        ;;

    # --- File ownership ---
    chown-app)
        validate_name "$2"
        chown -R "netrunner-$2:netrunner-$2" "$APPS_ROOT/$2"
        # env stays root-owned
        if [[ -f "$APPS_ROOT/$2/env" ]]; then
            chown root:root "$APPS_ROOT/$2/env"
            chmod 600 "$APPS_ROOT/$2/env"
        fi
        ;;

    # --- Env file management ---
    write-env)
        validate_name "$2"
        # Read content from stdin, write to env file as root
        cat > "$APPS_ROOT/$2/env"
        chown root:root "$APPS_ROOT/$2/env"
        chmod 600 "$APPS_ROOT/$2/env"
        ;;

    *)
        echo "Unknown command: ${1:-}" >&2
        exit 1
        ;;
esac
```

### sudoers configuration

```
# /etc/sudoers.d/netrunner
# Single rule — all privileged ops go through the validated wrapper
netrunner ALL=(root) NOPASSWD: /opt/netrunner/netrunner-sudo.sh *
```

The wrapper script:
- Validates app names against `^[a-z0-9][a-z0-9-]{0,46}[a-z0-9]$` — no path traversal possible
- Constructs all paths internally from validated names — no user-controlled paths reach filesystem operations
- Handles env file writes via stdin, maintaining `root:root 600` ownership
- Is owned by `root:root` with mode `755` and immutable attribute (`chattr +i`)

### How SystemdService calls the wrapper

```csharp
// Instead of: sudo systemctl restart netrunner-{name}
// Call:        sudo /opt/netrunner/netrunner-sudo.sh restart {name}

public async Task Restart(string appName)
{
    // appName already validated in AppManager
    await RunSudo("restart", appName);
}

private async Task<string> RunSudo(params string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = "sudo",
        Arguments = $"/opt/netrunner/netrunner-sudo.sh {string.Join(" ", args)}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    // ...
}
```

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

### Forgejo Webhook: clone URL

When processing a webhook, the clone URL from the payload is used **only for lookup** — to match the repository to a registered app via the `[webhooks]` mapping in `netrunner.conf`.

The actual `git clone` command always uses the URL stored in `netrunner.conf`, never the one from the payload. This prevents an attacker who knows the webhook secret from injecting a malicious repository URL.
