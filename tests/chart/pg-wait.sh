#!/usr/bin/env bash
# Waits until PostgreSQL in a container accepts connections over TCP (initdb's first server listens on the socket
# only, so a socket check would pass too early). Used by CI's "Backup and restore" job and tests/chart/backup-test.sh.
#   pg-wait.sh <container> <user> <database> [seconds, default 60]
set -euo pipefail
container="$1" user="$2" database="$3" seconds="${4:-60}"
timeout "$seconds" bash -c 'until docker exec "$0" pg_isready -q -h 127.0.0.1 -U "$1" -d "$2"; do sleep 0.5; done' \
  "$container" "$user" "$database" || { echo "PostgreSQL in $container wasn't ready within ${seconds}s." >&2; exit 1; }
