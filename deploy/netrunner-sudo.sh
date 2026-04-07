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
        cat > "$APPS_ROOT/$2/env"
        chown root:root "$APPS_ROOT/$2/env"
        chmod 600 "$APPS_ROOT/$2/env"
        ;;

    *)
        echo "Unknown command: ${1:-}" >&2
        exit 1
        ;;
esac
