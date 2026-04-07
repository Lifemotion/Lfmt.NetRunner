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
dotnet publish -c Release -o /var/lib/netrunner/apps/{name}/releases/v_new/
    │  (if build fails — report error, clean up source/ and v_new/)
    ▼
Generate/copy .service → install via wrapper script
    │
    ▼
Atomic symlink swap to v_new/:
    ln -s v_new current.tmp && mv -T current.tmp current
    │
    ▼
sudo netrunner-sudo.sh daemon-reload
sudo netrunner-sudo.sh restart {name}
    │
    ▼
Health check (loop)
    │
    ├── OK:
    │   Finalize release rotation:
    │       rm -rf v1/
    │       mv v2/ v1/  (if exists)
    │       mv v_new/ v2/
    │       ln -s v2 current.tmp && mv -T current.tmp current
    │   Log to deploy.log, clean up source/
    │
    └── FAIL → auto-rollback (see below)
```

### Why this order matters

The build goes into `v_new/` — a temporary slot. The symlink switches to `v_new/` for testing, but `v1/` and `v2/` remain untouched. Only after a successful health check does the rotation happen. If the process crashes at any point:

- `v2/` (the last known-good release) is still intact
- `v1/` (the previous release) is still intact
- Recovery: just point `current` back to `v2/`

### Atomic symlink swap

`ln -sfn` is **not** atomic — it calls `unlink()` + `symlink()`, leaving a gap where `current` doesn't exist. Instead:

```bash
ln -s v2 current.tmp    # create new symlink with temp name
mv -T current.tmp current  # rename(2) — atomic on Linux
```

`mv -T` uses `rename(2)`, which atomically replaces the old symlink.

## 2. Forgejo Webhook

```
Forgejo push → POST /api/webhook/forgejo
    │
    ▼
Verify HMAC-SHA256 signature (X-Forgejo-Signature)
    │  (on mismatch → 403)
    ▼
Parse payload: extract repo clone_url and branch
    │
    ▼
Lookup: match payload clone_url → app_name (from netrunner.conf [webhooks])
    │  (if no mapping → 404)
    ▼
Check branch (deploy only from configured branch, default: main)
    │  (other branch → 200 OK, no action)
    ▼
git clone using the URL from netrunner.conf (NOT from payload):
    git clone --depth 1 --branch {branch} {config_clone_url} .../source/
    │
    ▼
Continue from step 3 of the Archive Upload flow
```

**Important:** The clone URL from the webhook payload is used only to look up the app name in the `[webhooks]` mapping. The actual `git clone` always uses the URL stored in `netrunner.conf`. This prevents an attacker who knows the webhook secret from injecting a malicious repository.

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
Does v2/ (previous known-good version) exist?
    │
    ├── Yes:
    │   Atomic symlink swap back to v2/:
    │       ln -s v2 current.tmp && mv -T current.tmp current
    │   sudo netrunner-sudo.sh restart {name}
    │   Health check v2
    │       │
    │       ├── OK → deploy.log: "ROLLBACK SUCCESSFUL"
    │       │        Clean up v_new/
    │       │
    │       └── FAIL → sudo netrunner-sudo.sh stop {name}
    │                  deploy.log: "ROLLBACK FAILED, SERVICE STOPPED"
    │
    └── No (first deploy):
        sudo netrunner-sudo.sh stop {name}
        deploy.log: "FIRST DEPLOY FAILED, NO ROLLBACK, SERVICE STOPPED"
```

Note: during rollback, `v_new/` still exists (the failed release). It is cleaned up after successful rollback, or kept for debugging if rollback also fails.

## 5. Manual Rollback

Via UI (Rollback button) or API (`POST /api/apps/{name}/rollback`):

1. `ln -s v1 current.tmp && mv -T current.tmp current`
2. `sudo netrunner-sudo.sh restart {name}`
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
