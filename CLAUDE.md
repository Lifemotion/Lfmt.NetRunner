# CLAUDE.md

## Project Overview

Lfmt.NetRunner is a lightweight self-hosted service for deploying .NET Core applications on Linux. It manages the full app lifecycle: upload/webhook → build → deploy → health check → rollback. Built on ASP.NET Core 10.0 with Razor Pages + API Controllers, PicoCSS, file-based storage (INI + JSON Lines).

## Build & Run

```bash
# Build
dotnet build

# Run locally (Windows, dev mode — sudo calls are stubbed)
dotnet run --project Lfmt.NetRunner

# Run under WSL (real systemd)
# Requires setup: see docs/server-setup.md
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5050 dotnet run --project Lfmt.NetRunner

# Pack test app
testapp\pack-source.cmd    # source archive
testapp\pack-publish.cmd   # pre-built artifact archive
```

## Architecture

Single project, no layered separation.

- **Controllers/** — `AppsController` (app CRUD, deploy, rollback), `WebhookController` (Forgejo)
- **Filters/** — `ApiExceptionFilter` (global JSON error responses)
- **Models/** — `AppConfig`, `AppState`, `UiSettings`, `NetRunnerConfig`, DTOs
- **Services/** — `AppManager`, `DeployService`, `SystemdService`, `HealthCheckService`, `ServiceFileGenerator`, `ForgejoService`, `SettingsService`, `IniParser`
- **Pages/** — Dashboard, App Detail/Create/Deploy/Edit/Env, Settings, Logs

## Key Design Decisions

- **File-based storage**: INI for configs, JSON Lines for deploy logs. No database.
- **Two deploy modes**: `project` field → source (NetRunner builds), `dll` field → artifact (deploy directly)
- **Per-app system users**: `netrunner-{name}` for isolation
- **Privileged wrapper script**: all sudo calls go through `netrunner-sudo.sh` with input validation
- **Dev mode on Windows**: `SystemdService` returns stubs when `!OperatingSystem.IsLinux()`
- **Config path**: Linux → `/var/lib/netrunner/netrunner.conf`, Windows → `netrunner.dev.conf`

## Deployment

- **Install**: `curl -sSL .../deploy/install.sh | bash`
- **Update**: `curl -sSL .../deploy/update.sh | bash`
- **Release**: push tag `v*` → GitHub Actions builds + publishes release

## Important Notes

- NetRunner's own service file must NOT have `NoNewPrivileges`, `ProtectSystem`, or `ProtectHome` — it needs sudo for user/service management
- `obj/` directories are cleaned before source builds to avoid cross-platform NuGet path issues
- `DOTNET_CLI_HOME` and `HOME` must be set in service file (netrunner user has no home dir)
- Managed apps get full systemd hardening in their generated service files
- `env` files are `root:root 600`, read/written via sudo wrapper
- App directories use setgid (`g+s`) so new files inherit group `netrunner`
