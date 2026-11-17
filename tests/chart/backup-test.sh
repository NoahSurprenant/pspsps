#!/usr/bin/env bash
# Runs the chart's backup script (charts/pspsps/files/backup.sh) the way its CronJob does - the chart's PostgreSQL
# image, as uid 70, read-only root, no capabilities - against a real PostgreSQL, then restores dumps the way
# docs/backups.md says. CI runs it against a container of the same image:
#   PG_IMAGE       the chart's postgres.image (repository:tag)
#   PG_CONTAINER   the PostgreSQL container's name or ID (for docker exec, like kubectl exec into the database's pod)
#   PGPASSWORD     the password of PGUSER (default pspsps), which owns PGDATABASE (default pspsps), on localhost:5432
set -euo pipefail

: "${PG_IMAGE:?PG_IMAGE is not set}"
: "${PG_CONTAINER:?PG_CONTAINER is not set}"
: "${PGPASSWORD:?PGPASSWORD is not set}"
user="${PGUSER:-pspsps}"
db="${PGDATABASE:-pspsps}"
root="$(cd "$(dirname "$0")/../.." && pwd)"

backups="$(mktemp -d)"
trap 'rm -rf "$backups" 2>/dev/null || sudo rm -rf "$backups"' EXIT
# The pod's fsGroup makes the volume writable by uid 70; here, anyone may write.
chmod 0777 "$backups"

fail() { echo "FAIL: $*" >&2; exit 1; }

# psql inside the database's container, over its socket: what kubectl exec does.
sql() { docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -qtA -U "$user" -d "${2:-$db}" -c "$1"; }
pg() { docker exec -i "$PG_CONTAINER" "$@"; }

backup() { # backup <keep> [password]
  docker run --rm --network host --user 70:70 --read-only --tmpfs /tmp --cap-drop ALL \
    --security-opt no-new-privileges \
    -v "$root/charts/pspsps/files/backup.sh:/scripts/backup.sh:ro" -v "$backups:/backups" \
    -e PGHOST=localhost -e PGPORT=5432 -e PGUSER="$user" -e PGDATABASE="$db" -e PGPASSWORD="${2:-$PGPASSWORD}" \
    -e BACKUP_DIR=/backups -e BACKUP_KEEP="$1" -e BACKUP_STALE_SECONDS=3600 \
    "$PG_IMAGE" /bin/sh /scripts/backup.sh
}

dumps() { find "$backups" -maxdepth 1 -name 'pspsps-*.dump' -printf '%f\n' | sort; }
count() { dumps | wc -l; }

echo "== Some data, and the migrations so far"
sql "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" text PRIMARY KEY, \"ProductVersion\" text NOT NULL);"
sql "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260101000000_Initial', '10.0.0');"
sql "CREATE TABLE backup_test (id int PRIMARY KEY, name text NOT NULL);"
sql "INSERT INTO backup_test SELECT i, 'cat ' || i FROM generate_series(1, 100) i;"

echo "== Runs keeping two"
backup 2
first="$(dumps | head -n 1)"
for _ in 1 2; do
  sleep 1 # dumps are named to the second
  backup 2
done
[ "$(count)" -eq 2 ] || fail "expected 2 dumps, got: $(dumps | tr '\n' ' ')"
dumps | grep -qx "$first" && fail "the oldest dump ($first) was not deleted"
newest="$(dumps | tail -n 1)"

echo "== Retention runs before the dump, so a full volume still rotates: a failed run leaves keep - 1"
sleep 1
if backup 2 wrong-password; then fail "a run with a wrong password succeeded"; fi
[ "$(count)" -eq 1 ] || fail "a failed run keeping 2 should leave 1 dump, got: $(dumps | tr '\n' ' ')"
[ "$(dumps)" = "$newest" ] || fail "a failed run deleted the newest dump"
sleep 1
backup 2
[ "$(count)" -eq 2 ] || fail "a run after a failed one should leave 2 dumps, got: $(dumps | tr '\n' ' ')"
newest="$(dumps | tail -n 1)"

echo "== Another run's .partial survives; a stale one is cleared"
fresh="pspsps-20991231T235959Z.dump.other-pod.1.partial"
stale="pspsps-20000101T000000Z.dump.dead-pod.1.partial"
touch "$backups/$fresh"
touch -d '2 hours ago' "$backups/$stale"
sleep 1
backup 2
[ -e "$backups/$fresh" ] || fail "a run deleted another run's in-progress $fresh"
[ ! -e "$backups/$stale" ] || fail "a run left the stale $stale"
[ "$(find "$backups" -maxdepth 1 -name '*.partial' | wc -l)" -eq 1 ] || fail "a run left its own .partial"
rm -f "$backups/$fresh"
newest="$(dumps | tail -n 1)"

echo "== Bad BACKUP_KEEP is refused before anything is deleted"
for bad in 0 00 08 -1 x ""; do
  if backup "$bad"; then fail "BACKUP_KEEP='$bad' was accepted"; fi
  [ "$(count)" -eq 2 ] || fail "BACKUP_KEEP='$bad' changed the dumps: $(dumps | tr '\n' ' ')"
  [ "$(dumps | tail -n 1)" = "$newest" ] || fail "BACKUP_KEEP='$bad' wrote a dump"
done

# Out of the backup volume as its owner (kubectl exec into a pod mounting it), into the database's pod.
dump() { docker run --rm --user 70:70 -v "$backups:/backups:ro" "$PG_IMAGE" cat "/backups/$newest"; }

echo "== Restore into a copy"
pg createdb -U "$user" restore_test
dump | pg pg_restore -U "$user" -d restore_test --no-owner --exit-on-error
[ "$(sql "SELECT count(*), sum(id) FROM backup_test;" restore_test)" = "100|5050" ] || fail "the copy is missing rows"
pg dropdb -U "$user" restore_test

echo "== Restore in place, over a newer schema"
# What an upgrade after the dump leaves: a newer migration, its table pointing at an old one, and changed data.
sql "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260201000000_Newer', '10.0.0');"
sql "CREATE TABLE newer (id int PRIMARY KEY, cat int NOT NULL REFERENCES backup_test (id));"
sql "INSERT INTO newer VALUES (1, 1);"
sql "DELETE FROM backup_test WHERE id > 10; INSERT INTO backup_test VALUES (1000, 'not in the dump');"
dump | pg pg_restore -U "$user" -d "$db" --clean --if-exists --no-owner --single-transaction --exit-on-error
[ "$(sql "SELECT count(*), sum(id) FROM backup_test;")" = "100|5050" ] || fail "the restore did not bring the rows back"
[ -z "$(sql "SELECT to_regclass('public.newer');")" ] || fail "the newer table survived the restore"
[ "$(sql "SELECT string_agg(\"MigrationId\", ',') FROM \"__EFMigrationsHistory\";")" = "20260101000000_Initial" ] \
  || fail "the migration history is not the dump's"
[ "$(sql "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$db';")" = "$user" ] \
  || fail "the restored database is not owned by $user"

echo "OK: dumps, retention, partial files, failed runs and both restores work."
