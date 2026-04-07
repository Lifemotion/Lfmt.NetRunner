# Lfmt.NetRunner

A lightweight self-hosted service for deploying .NET Core applications on Linux.

## Features

- **Two deploy modes**: source (NetRunner builds on server) or pre-built artifacts
- **Web UI**: dashboard, app management, logs, settings
- **REST API**: full control via API, Forgejo webhook integration
- **Security**: per-app system users, systemd hardening, privileged wrapper script
- **Auto-rollback**: health check after deploy, automatic rollback on failure
- **Zero-downtime deploys**: atomic symlink swap, crash-safe version rotation

## Quick Install

Requires .NET 10 SDK on the server (for building apps from source).

```bash
# Install .NET SDK
sudo apt install -y dotnet-sdk-10.0

# Install NetRunner
curl -sSL https://raw.githubusercontent.com/OWNER/Lfmt.NetRunner/main/deploy/install.sh | bash
```

## Update

```bash
curl -sSL https://raw.githubusercontent.com/OWNER/Lfmt.NetRunner/main/deploy/update.sh | bash
```

## Usage

1. Create a `.netrunner` file in your project root:

```ini
[app]
name = my-app
port = 5100
project = MyApp/MyApp.csproj

[health]
path = /health
phrase = Healthy
```

2. Deploy via UI or API:

```bash
# Via API
zip -r app.zip .netrunner MyApp/
curl -X POST http://localhost:5050/api/apps/create -F "file=@app.zip"
```

3. Manage via dashboard at `http://localhost:5050`

## Documentation

- [Architecture](docs/architecture.md)
- [.netrunner file spec](docs/netrunner-file.md)
- [Security model](docs/security.md)
- [Deployment flow](docs/deployment-flow.md)
- [Server setup](docs/server-setup.md)
- [Design decisions](docs/decisions.md)
