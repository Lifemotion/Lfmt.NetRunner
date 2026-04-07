# Deployment Flow

## 1. Archive Upload (Web UI)

```
User → /App/{name}/Deploy → uploads .tar.gz/.zip
    │
    ▼
Extract to /var/lib/netrunner/apps/{name}/source/
    │
    ▼
Read .netrunner from extracted source root
    │  (if missing — error)
    ▼
Validate: port not conflicting, name matches, .csproj exists
    │
    ▼
dotnet publish -c Release -o <temp-output>
    │  (if build fails — report error, clean up source/)
    ▼
Release rotation:
    rm -rf v1/ → mv v2/ v1/ → mv <output> v2/
    │
    ▼
ln -sfn v2/ current    (atomic symlink swap)
    │
    ▼
Generate/copy .service → /etc/systemd/system/netrunner-{name}.service
    │
    ▼
sudo systemctl daemon-reload
sudo systemctl restart netrunner-{name}
    │
    ▼
Health check (loop)
    │
    ├── OK → log to deploy.log, clean up source/
    │
    └── FAIL → auto-rollback (see below)
```

## 2. Forgejo Webhook

```
Forgejo push → POST /api/webhook/forgejo
    │
    ▼
Verify HMAC-SHA256 signature (X-Forgejo-Signature)
    │  (on mismatch → 403)
    ▼
Parse payload: clone_url, branch, commit
    │
    ▼
Map repo_url → app_name (from netrunner.conf [webhooks])
    │  (if no mapping → 404)
    ▼
Check branch (deploy only from configured branch, default: main)
    │  (other branch → 200 OK, no action)
    ▼
git clone --depth 1 --branch {branch} {clone_url} .../source/
    │
    ▼
Continue from step 3 of the Archive Upload flow
```

### Webhook Mapping in netrunner.conf

```ini
[webhooks]
# clone_url = app-name
https://git.example.com/user/my-app.git = my-web-app
https://git.example.com/user/other.git = other-app
```

## 3. Health Check

After `systemctl restart`:

```
attempts = ceil(timeout / interval)

for i in 1..attempts:
    wait(interval seconds)
    response = HTTP GET http://localhost:{port}{health_path}
    if response.status == 200 AND response.body.contains(health_phrase):
        return SUCCESS

return FAILURE
```

Parameters are taken from the `.netrunner` `[health]` section:
- `path` — request URL (default `/health`)
- `phrase` — string in response body (default `Healthy`)
- `timeout` — total timeout in seconds (default 30)
- `interval` — pause between attempts (default 3)

## 4. Auto-Rollback

On health check failure:

```
Health check FAILED
    │
    ▼
Does v1/ (previous version) exist?
    │
    ├── Yes:
    │   ln -sfn v1/ current
    │   sudo systemctl restart netrunner-{name}
    │   Health check v1
    │       │
    │       ├── OK → deploy.log: "ROLLBACK SUCCESSFUL"
    │       │
    │       └── FAIL → sudo systemctl stop netrunner-{name}
    │                  deploy.log: "ROLLBACK FAILED, SERVICE STOPPED"
    │
    └── No (first deploy):
        sudo systemctl stop netrunner-{name}
        deploy.log: "FIRST DEPLOY FAILED, NO ROLLBACK, SERVICE STOPPED"
```

## 5. Manual Rollback

Via UI (Rollback button) or API (`POST /api/apps/{name}/rollback`):

1. `ln -sfn v1/ current`
2. `sudo systemctl restart netrunner-{name}`
3. Health check runs → result logged
4. No automatic cascading rollback

## 6. deploy.log Format

JSON Lines — one JSON object per line:

```json
{"timestamp":"2026-04-07T14:30:00Z","action":"DEPLOY","result":"SUCCESS","message":"Built and deployed from archive","commit":null}
{"timestamp":"2026-04-07T14:30:25Z","action":"HEALTH_CHECK","result":"SUCCESS","message":"GET /health -> 200, phrase found","commit":null}
{"timestamp":"2026-04-08T10:00:00Z","action":"DEPLOY","result":"FAILURE","message":"Health check failed after 30s","commit":"abc1234"}
{"timestamp":"2026-04-08T10:00:35Z","action":"ROLLBACK","result":"SUCCESS","message":"Rolled back to v1","commit":null}
```
