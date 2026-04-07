# Design Decisions

## 1. Single project instead of layered architecture

**Decision:** One ASP.NET Core project without Core/Data/Web separation.

**Why:** Expected size is ~1500 LOC. Layer separation would add boilerplate without benefit. All logic lives in `Services/`, data shapes in `Models/`.

## 2. Files instead of a database

**Decision:** INI files for configuration, JSON Lines for logs. No SQLite/PostgreSQL.

**Why:** Unix way — text files, each with a single purpose. A database is overkill for 5-20 apps. INI format is native to Linux (systemd, desktop files). JSON Lines is append-only and greppable.

## 3. Dedicated system user per application

**Decision:** On app creation, NetRunner creates a `netrunner-{app-name}` system user. The app runs under this user.

**Why:** Isolation. If all apps run under the same user, each can read env files and `/proc/{pid}/environ` of the others. A dedicated user eliminates cross-app access.

**Implementation:**
- `sudo useradd -r -s /usr/sbin/nologin netrunner-{name}` on creation
- `sudo userdel netrunner-{name}` on deletion
- App files are owned by this user
- `env` file is owned by `root:root`, `chmod 600` (systemd reads it as root before switching to the app user)

## 4. Symlink-based release switching

**Decision:** `current` is a symlink to the active release. Switching uses atomic `rename(2)` via `ln -s <target> current.tmp && mv -T current.tmp current`.

**Why:** Zero downtime during file copy. systemd `WorkingDirectory` and `ExecStart` point through `current`, so a symlink swap + `systemctl restart` is all that's needed.

**Note:** `ln -sfn` is NOT atomic — it calls `unlink()` + `symlink()`, leaving a gap. `mv -T` uses `rename(2)`, which is truly atomic on Linux filesystems.

## 5. Two release slots (v1/v2) with crash-safe rotation

**Decision:** Keep only the current and previous versions. New builds go into a temporary `v_new/` slot; rotation to `v1`/`v2` happens only after a successful health check.

**Why:** One rollback level is sufficient for a self-hosted tool. Building into `v_new/` first ensures that `v1` and `v2` remain intact if the process crashes mid-deploy.

**Rotation (after successful health check):**
1. `rm -rf v1/`
2. `mv v2/ v1/` (if exists)
3. `mv v_new/ v2/`
4. Atomic symlink swap to `v2/`

If the deploy fails or the process crashes at any point before finalization, `v2/` (the last known-good release) is still available for recovery.

## 6. `netrunner-` prefix for systemd units

**Decision:** All managed services are named `netrunner-{app-name}.service`.

**Why:** Allows scoping sudoers with a `netrunner-*` wildcard. NetRunner cannot manage arbitrary system services — only its own.

## 7. Health check: HTTP request with phrase verification

**Decision:** After deployment, a loop of GET requests to the app. Verifies both status code 200 AND the presence of a configured phrase in the response body.

**Why:** Checking only the process (systemd alive) is insufficient — the app could be hanging with an error. HTTP request + phrase confirms the app is actually working and responding correctly.

## 8. Auto-rollback on health check failure

**Decision:** If health check fails, automatically roll back to v1 (symlink swap + restart). If v1 also fails, the service is stopped.

**Why:** Minimize downtime without manual intervention. Rolling back to a known-good version is safer than leaving a broken one running.

## 9. No authentication, IP restriction only

**Decision:** No login/password. NetRunner listens only on the specified IP:port (default `127.0.0.1:5050`).

**Why:** The service sits behind Teleport proxy, which handles authentication. IP restriction is an additional layer: even if Teleport is compromised, NetRunner is unreachable from outside.

## 10. Two deployment methods: archive upload and Forgejo webhook

**Decision:** Web UI for manual .tar.gz/.zip upload + POST endpoint for Forgejo webhooks.

**Why:** Manual upload is a simple option for one-off deployments. Webhook automates deployment on push, without a separate CI/CD service.

## 11. Server-side compilation

**Decision:** .NET SDK is installed on the server. NetRunner runs `dotnet publish -c Release` from source.

**Why:** .NET Core apps compile quickly and simply. No separate build server needed. SDK on the server (~500 MB) is an acceptable trade-off for simplicity.

## 12. Hand-rolled INI parser

**Decision:** ~50 lines of C# instead of a third-party library.

**Why:** The format is trivial: sections, key-value pairs, comments. A NuGet dependency for this is overkill.

## 13. Secret env file: root:root, chmod 600

**Decision:** The `env` file is owned by root, not directly readable by the application.

**Why:** systemd reads `EnvironmentFile` as PID 1 (root) before switching to the service user. Variables are passed into the process environment, but the file itself remains protected. The app gets its own variables but cannot read other apps' env files.

## 14. App configuration via `.netrunner` file

**Decision:** An INI file named `.netrunner` in the repository/archive root describes deployment parameters.

**Why:** Convention over configuration. The developer places `.netrunner` in the repo — NetRunner knows how to build and deploy the app. Analogous to `Dockerfile`, `Procfile`, `app.yaml`.

## 15. Service file generation with custom override

**Decision:** Default generation from a built-in template. Optionally, a custom .service file via `custom_file` in `.netrunner`. Security-critical directives (`User=`, `Group=`, hardening) are **always force-overridden** from the template, even in custom files.

**Why:** 90% of apps are standard — the template covers them. For non-standard cases (special mounts, dependencies), partial customization is available. Force-overriding security directives prevents privilege escalation via a malicious custom service file.

## 16. Privileged wrapper script instead of direct sudo

**Decision:** All privileged operations go through a single bash script (`/opt/netrunner/netrunner-sudo.sh`) with strict input validation. sudoers grants access only to this script.

**Why:** Wildcard `*` in sudoers matches `/`, enabling path traversal attacks (e.g., `chown -R ... /var/lib/netrunner/apps/../../etc`). The wrapper validates app names against `^[a-z0-9][a-z0-9-]{0,46}[a-z0-9]$` and constructs all paths internally, eliminating path traversal.

## 17. Webhook clone URL from config, not payload

**Decision:** The clone URL from a Forgejo webhook payload is used only for lookup (matching to an app name). The actual `git clone` uses the URL stored in `netrunner.conf`.

**Why:** An attacker who knows the webhook secret could send a forged payload with a malicious repository URL. Using the config URL ensures only pre-approved repositories are cloned and built.
