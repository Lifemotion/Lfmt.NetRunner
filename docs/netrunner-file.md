# `.netrunner` File Specification

The `.netrunner` file is an application descriptor for Lfmt.NetRunner. It is placed in the root of the repository or archive.

## Format

INI-like:

- Encoding: UTF-8, no BOM
- Lines starting with `#` are comments
- Empty lines are ignored
- `[section]` starts a section
- `key = value` within a section (whitespace around `=` is trimmed)
- All values are strings; the consumer parses `256M`, `50%`, etc.

## Sections

### [app] — required

```ini
[app]
# Application name (lowercase letters, digits, hyphens). Used as:
# - directory name in /var/lib/netrunner/apps/
# - systemd unit name: netrunner-{name}.service
# - system user name: netrunner-{name}
name = my-web-app

# HTTP port the application listens on
port = 5100

# Path to .csproj relative to repository/archive root
project = src/MyApp/MyApp.csproj
```

**Name validation:**
- Only `[a-z0-9-]`
- Must not start or end with a hyphen
- Length: 2-48 characters
- Must be unique among registered apps

**Port validation:**
- Range: 1024-65535
- Must not conflict with another registered app

### [health] — optional

```ini
[health]
# Health check path (relative to http://localhost:{port})
# Default: /health
path = /health

# String that must be present in the response body
# Default: Healthy
phrase = Healthy

# Timeout in seconds: how long to wait after startup
# Default: 30
timeout = 30

# Interval between attempts in seconds
# Default: 3
interval = 3
```

### [resources] — optional

```ini
[resources]
# Memory limit (systemd MemoryMax)
# Formats: 128M, 1G, 512M
# Default: 256M
memory = 256M

# CPU limit (systemd CPUQuota)
# 100% = one core, 200% = two cores
# Default: 100%
cpu = 100%
```

### [env] — optional

```ini
[env]
# Environment variables added to the .service file
# NOT for secrets — those are set via the UI (env file)
ASPNETCORE_ENVIRONMENT = Production
MY_CONFIG_VALUE = some-value
```

Variables from this section are written as `Environment=KEY=VALUE` in the .service file.
Secrets (API keys, passwords) are set separately via the UI → saved to the `env` file → loaded via `EnvironmentFile`.

### [service] — optional

```ini
[service]
# Path to a custom .service file (relative to repo root)
# If specified, the template is not used — this file is used instead
# {{placeholder}} substitutions work in custom files too
# custom_file = deploy/my-app.service

# Extra directives for the [Service] section
# Appended to the end of the generated .service file
# extra_directives = ReadWritePaths=/var/data/my-app
```

## Full Example

```ini
# .netrunner — RW.HelpDesk

[app]
name = rw-helpdesk
port = 5035
project = RW.HelpDesk/RW.HelpDesk.csproj

[health]
path = /
phrase = HelpDesk
timeout = 45
interval = 5

[resources]
memory = 512M
cpu = 100%

[env]
ASPNETCORE_ENVIRONMENT = Production

[service]
extra_directives = ReadWritePaths=/var/data/rw-helpdesk
```

## Template Placeholders

When generating a .service file (or processing a custom one), the following placeholders are available:

| Placeholder | Source | Example |
|---|---|---|
| `{{app_name}}` | [app] name | `my-web-app` |
| `{{port}}` | [app] port | `5100` |
| `{{memory}}` | [resources] memory | `256M` |
| `{{cpu}}` | [resources] cpu | `100%` |
| `{{dotnet_path}}` | netrunner.conf paths.dotnet_path | `/usr/bin/dotnet` |
| `{{dll_name}}` | Derived from .csproj filename | `MyApp.dll` |
| `{{extra_directives}}` | [service] extra_directives | `ReadWritePaths=...` |
