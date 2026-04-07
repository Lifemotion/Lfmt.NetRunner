# Lfmt.NetRunner — Architecture

A lightweight self-hosted service for deploying .NET Core applications on Linux.
Manages the full application lifecycle: receiving, building, deploying, monitoring, and rolling back.

## Stack

- ASP.NET Core 10.0, Razor Pages
- PicoCSS (UI without JS frameworks)
- Data storage: files (INI + JSON Lines)
- Target OS: Debian 13+

## Project Structure

Single project — the tool is small enough that layered architecture would be unnecessary overhead.

```
Lfmt.NetRunner/
    Lfmt.NetRunner.sln
    Lfmt.NetRunner/
        Lfmt.NetRunner.csproj
        Program.cs
        appsettings.json
        appsettings.Development.json
        appsettings.Production.json
        libman.json
        wwwroot/
            lib/picocss/
        Pages/
            Shared/_Layout.cshtml
            Index.cshtml                # Dashboard: app list + status
            AppDetail.cshtml            # App details: logs, status, actions
            AppCreate.cshtml            # Register a new app
            AppEdit.cshtml              # Edit app configuration
            Deploy.cshtml               # Upload archive for deployment
            AppEnv.cshtml               # Edit environment variables
            Logs.cshtml                 # Global deployment log
        Models/
            AppConfig.cs                # Deserialized .netrunner file
            AppState.cs                 # Runtime state (running/stopped/deploying)
            DeploymentLogEntry.cs       # Entry in deploy.log
            ForgejoWebhookPayload.cs    # Forgejo webhook DTO
            NetRunnerConfig.cs          # NetRunner's own configuration
        Services/
            IniParser.cs                # INI parser (~50 lines, no dependencies)
            AppManager.cs               # App CRUD, config read/write
            DeployService.cs            # Build, deploy, rollback
            HealthCheckService.cs       # HTTP health check after deployment
            SystemdService.cs           # Wrapper around sudo systemctl
            ServiceFileGenerator.cs     # Generate .service from template
            ForgejoService.cs           # Forgejo webhook, git clone
        Templates/
            app.service.template        # Systemd unit template
    docs/
        architecture.md                 # This file
        decisions.md                    # Design decisions
        netrunner-file.md               # .netrunner file specification
        security.md                     # Security model
        deployment-flow.md              # Deployment flow
        server-setup.md                 # Server setup guide
    deploy/
        netrunner.service               # Systemd unit for NetRunner itself
        netrunner-sudoers               # sudoers file
```

## Services

### AppManager (Singleton)

Manages application configurations. Reads all configs on startup, caches in memory.

- `GetAllApps()` — list all apps with their state
- `GetApp(name)` — single app config + state
- `CreateApp(AppConfig)` — create directories, write config.ini, create system user
- `UpdateApp(name, AppConfig)` — update config.ini
- `DeleteApp(name)` — stop service, remove systemd unit, user, and directory

### DeployService (Singleton)

Core deployment logic. Sequential execution per app (SemaphoreSlim per app name).

- `DeployFromArchive(name, Stream)` — extract, build, deploy
- `DeployFromGit(name, cloneUrl, branch)` — clone, build, deploy
- `Rollback(name)` — switch symlink back to previous version

### SystemdService (Singleton)

Wrapper around `sudo systemctl` and `sudo journalctl`.

- `Start/Stop/Restart/Enable/Disable(appName)`
- `GetStatus(appName)` — parse `systemctl status` output into structured data
- `GetJournalLogs(appName, lines)` — recent log lines from journalctl
- `DaemonReload()`
- `InstallServiceFile(appName)` — copy .service to /etc/systemd/system/

### HealthCheckService

HTTP health check after deployment.

- `CheckHealth(port, path, phrase, timeout, interval)` → bool
- Loop: GET http://localhost:{port}{path}, verify status 200 + phrase present in body

### ServiceFileGenerator

Generates .service from template with `{{placeholder}}` substitution.
If `.netrunner` specifies `custom_file` — uses that instead (with the same substitutions).

### ForgejoService (Singleton)

- `VerifySignature(payload, signature, secret)` — HMAC-SHA256
- `ParsePayload(json)` — deserialize webhook JSON
- `ResolveAppName(repoUrl)` — look up in webhook mapping

## Endpoints

### Razor Pages (UI)

| Route | Description |
|---|---|
| `/` | Dashboard — app list, status, port, last deploy time |
| `/App/{name}` | Details: status, deploy history, control buttons |
| `/App/Create` | Register new app form |
| `/App/{name}/Edit` | Edit configuration |
| `/App/{name}/Deploy` | Upload archive |
| `/App/{name}/Env` | Edit secrets (env file) |
| `/Logs` | Global deployment log across all apps |

### API (Minimal API)

| Method | Route | Description |
|---|---|---|
| POST | `/api/webhook/forgejo` | Receive Forgejo webhook |
| POST | `/api/apps/{name}/deploy` | Deploy from uploaded archive |
| POST | `/api/apps/{name}/start` | Start service |
| POST | `/api/apps/{name}/stop` | Stop service |
| POST | `/api/apps/{name}/restart` | Restart service |
| POST | `/api/apps/{name}/rollback` | Rollback to previous version |
| GET | `/api/apps/{name}/status` | Service status (JSON) |
| GET | `/api/apps/{name}/logs` | Logs from journalctl (JSON) |

The API is used by both UI buttons (fetch) and potential CLI automation.

## Server Data Layout

```
/var/lib/netrunner/
    netrunner.conf                  # NetRunner configuration
    apps/
        {app-name}/
            config.ini              # Copy of .netrunner (authoritative config)
            current -> releases/v2/ # Symlink to active release
            releases/
                v1/                 # Previous version (for rollback)
                v2/                 # Current version
            source/                 # Temporary: git clone or extracted archive
            env                     # Secrets (root:root, chmod 600)
            app.service             # Generated unit file
            deploy.log              # Deployment history (JSON Lines)
```

### Storage Principles

- `current` is a symlink. Version switching is an atomic symlink swap (`ln -sfn`).
- Two slots: `v1` (previous) and `v2` (current). On new deploy: v1 is deleted, v2 becomes v1, new build becomes v2.
- `source/` is a temporary workspace, deleted after build completes.
- `config.ini` is the authoritative config — a copy of `.netrunner` with possible edits from the UI.
- `env` holds secrets not included in `.netrunner`. Managed via UI.
- `deploy.log` uses JSON Lines format (one JSON object per line). Append-only.
