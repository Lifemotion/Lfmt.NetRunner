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
    start|restart)
        validate_name "$2"
        systemctl reset-failed "netrunner-$2" 2>/dev/null || true
        systemctl "$1" "netrunner-$2"
        ;;
    stop|enable|disable)
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
        if [[ ! "$lines" =~ ^[0-9]+$ ]]; then
            echo "Invalid line count: $lines" >&2
            exit 1
        fi
        journalctl -u "netrunner-$2" -n "$lines" --no-pager
        ;;

    # --- Cleanup before deploy ---
    clean-deploy)
        validate_name "$2"
        rm -rf "$APPS_ROOT/$2/source" "$APPS_ROOT/$2/releases/v_new"
        ;;

    # --- App directory setup ---
    init-app)
        validate_name "$2"
        mkdir -p "$APPS_ROOT/$2/releases" "$APPS_ROOT/$2/source"
        chown -R "netrunner-$2:netrunner" "$APPS_ROOT/$2"
        chmod -R g+rwx "$APPS_ROOT/$2"
        # Set setgid so new files/dirs inherit group
        find "$APPS_ROOT/$2" -type d -exec chmod g+s {} +
        ;;

    # --- User management ---
    create-user)
        validate_name "$2"
        id "netrunner-$2" &>/dev/null || useradd -r -s /usr/sbin/nologin "netrunner-$2"
        ;;
    delete-user)
        validate_name "$2"
        userdel "netrunner-$2" || true
        ;;

    # --- File ownership ---
    chown-app)
        validate_name "$2"
        chown -R "netrunner-$2:netrunner" "$APPS_ROOT/$2"
        chmod -R g+rwx "$APPS_ROOT/$2"
        # env stays root-owned
        if [[ -f "$APPS_ROOT/$2/env" ]]; then
            chown root:root "$APPS_ROOT/$2/env"
            chmod 600 "$APPS_ROOT/$2/env"
        fi
        ;;

    # --- Env file management ---
    read-env)
        validate_name "$2"
        cat "$APPS_ROOT/$2/env" 2>/dev/null || true
        ;;
    write-env)
        validate_name "$2"
        cat > "$APPS_ROOT/$2/env"
        chown root:root "$APPS_ROOT/$2/env"
        chmod 600 "$APPS_ROOT/$2/env"
        ;;

    *)
        echo "Unknown command: ${1:-}" >&2
        exit 1
        ;;
esac
