#!/usr/bin/env bash
# Runs the chart's backup script (charts/pspsps/files/backup.sh) the way its CronJob does - the PostgreSQL image, as
# uid 70, read-only root, no capabilities - against a real PostgreSQL, then restores a dump the way docs/backups.md
# says. CI runs it against a service container:
#   PG_CONTAINER   the PostgreSQL container's ID (for docker exec, like kubectl exec into the database's pod)
#   PGPASSWORD     the password of PGUSER (default pspsps), which owns PGDATABASE (default pspsps), on localhost:5432
set -euo pipefail

: "${PG_CONTAINER:?PG_CONTAINER is not set}"
: "${PGPASSWORD:?PGPASSWORD is not set}"
user="${PGUSER:-pspsps}"
db="${PGDATABASE:-pspsps}"
image="${PG_IMAGE:-docker.io/library/postgres:18-alpine}"
root="$(cd "$(dirname "$0")/../.." && pwd)"

backups="$(mktemp -d)"
trap 'rm -rf "$backups" 2>/dev/null || sudo rm -rf "$backups"' EXIT
# The pod's fsGroup makes the volume writable by uid 70; here, anyone may write.
chmod 0777 "$backups"

fail() { echo "FAIL: $*" >&2; exit 1; }

# psql and pg_restore inside the database's container, over its socket: what kubectl exec does.
sql() { docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -qtA -U "$user" -d "${2:-$db}" -c "$1"; }

backup() { # backup <keep> [password]
  docker run --rm --network host --user 70:70 --read-only --tmpfs /tmp --cap-drop ALL \
    --security-opt no-new-privileges \
    -v "$root/charts/pspsps/files/backup.sh:/scripts/backup.sh:ro" -v "$backups:/backups" \
    -e PGHOST=localhost -e PGPORT=5432 -e PGUSER="$user" -e PGDATABASE="$db" -e PGPASSWORD="${2:-$PGPASSWORD}" \
    -e BACKUP_DIR=/backups -e BACKUP_KEEP="$1" \
    "$image" /bin/sh /scripts/backup.sh
}

dumps() { find "$backups" -maxdepth 1 -name 'pspsps-*.dump' -printf '%f\n' | sort; }

echo "== Some data"
sql "CREATE TABLE backup_test (id int PRIMARY KEY, name text NOT NULL);"
sql "INSERT INTO backup_test SELECT i, 'cat ' || i FROM generate_series(1, 100) i;"

echo "== Three runs keeping two"
backup 2
first="$(dumps | head -n 1)"
for _ in 1 2; do
  sleep 1 # dumps are named to the second
  backup 2
done
[ "$(dumps | wc -l)" -eq 2 ] || fail "expected 2 dumps, got: $(dumps | tr '\n' ' ')"
dumps | grep -qx "$first" && fail "the oldest dump ($first) was not deleted"
newest="$(dumps | tail -n 1)"

echo "== A failed run keeps what was there"
sleep 1
if backup 2 wrong-password; then fail "a run with a wrong password succeeded"; fi
[ "$(dumps | tail -n 1)" = "$newest" ] || fail "a failed run changed the dumps: $(dumps | tr '\n' ' ')"
[ "$(dumps | wc -l)" -eq 2 ] || fail "a failed run deleted a dump"

echo "== BACKUP_KEEP 0 is refused"
if backup 0; then fail "BACKUP_KEEP=0 was accepted"; fi
[ "$(dumps | wc -l)" -eq 2 ] || fail "BACKUP_KEEP=0 deleted dumps"

# Out of the backup volume as its owner (kubectl cp from a pod mounting it), into pg_restore in the database's pod.
dump() { docker run --rm --user 70:70 -v "$backups:/backups:ro" "$image" cat "/backups/$newest"; }

echo "== Restore into a copy"
sql "CREATE DATABASE restore_test;"
dump | docker exec -i "$PG_CONTAINER" pg_restore -U "$user" -d restore_test --no-owner --exit-on-error
[ "$(sql "SELECT count(*), sum(id) FROM backup_test;" restore_test)" = "100|5050" ] || fail "the copy is missing rows"
sql "DROP DATABASE restore_test;"

echo "== Restore in place"
sql "DELETE FROM backup_test WHERE id > 10; INSERT INTO backup_test VALUES (1000, 'not in the dump');"
dump | docker exec -i "$PG_CONTAINER" \
  pg_restore -U "$user" -d "$db" --clean --if-exists --no-owner --single-transaction --exit-on-error
[ "$(sql "SELECT count(*), sum(id) FROM backup_test;")" = "100|5050" ] || fail "the restore did not bring the rows back"

echo "OK: dumps, retention, failed runs and both restores work."
