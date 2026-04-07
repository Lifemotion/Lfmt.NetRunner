# Lfmt.NetRunner — Architecture

A lightweight self-hosted service for deploying .NET Core applications on Linux.
Manages the full application lifecycle: receiving, building, deploying, monitoring, and rolling back.

## Stack

- ASP.NET Core 10.0, Razor Pages + API Controllers
- PicoCSS (UI without JS frameworks)
- Data storage: files (INI + JSON Lines)
- Target OS: Debian 13+ (development on Windows via WSL2)

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
        netrunner.dev.conf              # Dev config (Windows, uses ./dev-apps)
        libman.json
        wwwroot/
            lib/picocss/
            css/site.css
            js/site.js
        Controllers/
            AppsController.cs           # API: app management, deploy, rollback
            WebhookController.cs        # API: Forgejo webhook
        Filters/
            ApiExceptionFilter.cs       # Global JSON error responses for API
        Pages/
            Shared/_Layout.cshtml
            Index.cshtml                # Dashboard: app list, status, system info
            Settings.cshtml             # UI settings: timezone, limits, etc.
            Logs.cshtml                 # Global deployment log
            App/
                Create.cshtml           # Deploy new app (upload + .netrunner reference)
                Detail.cshtml           # App details: config, logs, actions
                Deploy.cshtml           # Re-deploy existing app
                Edit.cshtml             # Edit app configuration
                Env.cshtml              # Edit secret environment variables
        Models/
            AppConfig.cs                # Deserialized .netrunner file
            AppState.cs                 # Runtime state (running/stopped/deploying)
            DeploymentLogEntry.cs       # Entry in deploy.log
            ForgejoWebhookPayload.cs    # Forgejo webhook DTO
            NetRunnerConfig.cs          # NetRunner's own configuration + host IP
            UiSettings.cs              # UI settings (timezone, format, limits)
        Services/
            IniParser.cs                # INI parser (~50 lines, no dependencies)
            AppManager.cs               # App CRUD, config read/write, deploy log
            DeployService.cs            # Build, deploy, rollback, health check
            HealthCheckService.cs       # HTTP health check after deployment
            SystemdService.cs           # Wrapper around sudo (dev mode on Windows)
            ServiceFileGenerator.cs     # Generate .service from template
            ForgejoService.cs           # Forgejo webhook, clone URL resolution
            SettingsService.cs          # Read/write UI settings
    testapp/
        .netrunner.source               # Source mode descriptor
        .netrunner.publish              # Artifact mode descriptor
        pack-source.cmd                 # Pack source archive
        pack-publish.cmd                # Pack publish archive
        TestApp/                        # Minimal test application
    docs/                               # Architecture documentation
    deploy/
        netrunner.service               # Systemd unit for NetRunner itself
        netrunner-sudoers               # sudoers file
        netrunner-sudo.sh               # Privileged wrapper script
```

## Services

### AppManager (Singleton)

Manages application configurations. Reads all configs from disk on startup, caches in memory.

- `GetAllApps()` — list all apps with config and state
- `GetAppConfig(name)` / `GetAppState(name)` — single app info
- `CreateApp(AppConfig)` — create user, directories (via sudo), write config
- `UpdateApp(name, AppConfig)` — update config.ini
- `DeleteApp(name)` — stop service, remove systemd unit, user, and directory
- `AppendDeployLog()` / `GetDeployLog()` / `GetGlobalDeployLog()` — deploy history

### DeployService (Singleton)

Core deployment logic. Sequential execution per app (SemaphoreSlim per app name). Supports two modes:

- **Source mode**: archive contains source + `.netrunner` with `project` field → `dotnet publish` on server
- **Artifact mode**: archive contains publish output + `.netrunner` with `dll` field → copy directly

Methods:
- `DeployFromArchive(name, Stream, fileName)` — extract, optionally build, deploy
- `DeployFromGit(name, cloneUrl, branch)` — clone, build, deploy
- `Rollback(name)` — switch symlink back to previous version

### SystemdService (Singleton)

Wrapper around the privileged wrapper script (`netrunner-sudo.sh`). On Windows, returns stubs (dev mode).

- `Start/Stop/Restart/Enable/Disable(appName)`
- `GetAppStatus(appName)` — parse status into enum
- `GetJournalLogs(appName, lines)` — recent log lines
- `InitApp(appName)` — create app directories via sudo
- `CreateUser/DeleteUser(appName)` — manage system users
- `ChownApp(appName)` — set ownership (app user + netrunner group)
- `ReadEnv/WriteEnv(appName)` — manage secrets file (root:root 600)
- `InstallServiceFile(appName)` — copy .service to systemd
- `DaemonReload()` — reload systemd

### HealthCheckService

HTTP health check after deployment.

- `CheckHealth(port, path, phrase, timeout, interval)` → bool
- Loop: GET http://localhost:{port}{path}, verify status 200 + phrase in body

### ServiceFileGenerator

Generates .service from built-in template with `{{placeholder}}` substitution.
If `.netrunner` specifies `custom_file` — uses that instead (with the same substitutions).
Security-critical directives (`User=`, `Group=`, hardening) are always force-overridden.

### ForgejoService (Singleton)

- `VerifySignature(payload, signature, secret)` — HMAC-SHA256
- `ParsePayload(json)` — deserialize webhook JSON
- `ResolveAppName(repoUrl)` — look up in webhook mapping
- `GetConfigCloneUrl(appName)` — clone URL from config (not from payload)

### SettingsService (Singleton)

Reads/writes UI settings from `settings.ini` in apps root.

## Endpoints

### Razor Pages (UI)

| Route | Description |
|---|---|
| `/` | Dashboard — app list, status, mode, deploy result, system info, auto-refresh |
| `/App/{name}` | App detail: config, deploy log, journal logs, action buttons |
| `/App/Create` | Deploy new app from archive (with .netrunner format reference) |
| `/App/{name}/Deploy` | Re-deploy existing app from archive |
| `/App/{name}/Edit` | Edit app configuration |
| `/App/{name}/Env` | Edit secret environment variables |
| `/Settings` | UI settings: timezone, time format, refresh, limits |
| `/Logs` | Global deployment log across all apps |

### API (Controllers)

| Method | Route | Description |
|---|---|---|
| POST | `/api/apps/create` | Create + deploy from archive with .netrunner |
| POST | `/api/apps/{name}/deploy` | Deploy archive to existing app |
| POST | `/api/apps/{name}/start` | Start service |
| POST | `/api/apps/{name}/stop` | Stop service |
| POST | `/api/apps/{name}/restart` | Restart service |
| POST | `/api/apps/{name}/rollback` | Rollback to previous version |
| POST | `/api/apps/{name}/delete` | Delete app and all data |
| GET | `/api/apps/{name}/status` | Service status (JSON) |
| GET | `/api/apps/{name}/logs` | Journal logs (JSON) |
| POST | `/api/webhook/forgejo` | Forgejo push webhook |

All API errors return JSON `{"error": "..."}` via global exception filter.

## Server Data Layout

```
/var/lib/netrunner/
    netrunner.conf                  # NetRunner configuration
    apps/
        settings.ini                # UI settings (timezone, limits, etc.)
        {app-name}/
            config.ini              # Copy of .netrunner (authoritative config)
            current -> releases/v2/ # Symlink to active release
            releases/
                v_new/              # Temporary: new build (before health check)
                v1/                 # Previous version (for rollback)
                v2/                 # Current version
            source/                 # Temporary: git clone or extracted archive
            env                     # Secrets (root:root, chmod 600)
            app.service             # Generated unit file
            deploy.log              # Deployment history (JSON Lines)
```

### Storage Principles

- `current` is a symlink. Switching uses `ln -sfn` (atomic on Linux).
- New builds go into `v_new/`. Only after a successful health check does rotation happen: v1 deleted, v2 → v1, v_new → v2. This ensures v2 (last known-good) survives crashes during deploy.
- `source/` is a temporary workspace, deleted after build completes. `obj/` directories are cleaned before build to avoid cross-platform NuGet path issues.
- `config.ini` is the authoritative config — a copy of `.netrunner` with possible edits from the UI.
- `env` holds secrets not included in `.netrunner`. Managed via UI, read/written through sudo wrapper to maintain `root:root 600` ownership.
- `deploy.log` uses JSON Lines format (one JSON object per line). Append-only.
- App directories use setgid (`g+s`) so new files inherit group `netrunner`.

## Development

- **Windows**: dev mode — sudo calls return stubs, uses `netrunner.dev.conf` with `./dev-apps`
- **WSL**: full Linux mode with real systemd, uses `/var/lib/netrunner/netrunner.conf`
- Launch profiles: "Windows" (dev mode) and "WSL" (real systemd) in `launchSettings.json`
- Test app in `testapp/` with `pack-source.cmd` and `pack-publish.cmd` scripts
