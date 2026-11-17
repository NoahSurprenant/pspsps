#!/usr/bin/env bash
# Runs the chart's backup script (charts/pspsps/files/backup.sh) the way its CronJob does - the chart's PostgreSQL
# image, as uid 70, read-only root, no capabilities, the script as PID 1 - against a real PostgreSQL, then restores
# dumps the way docs/backups.md says. CI runs it against a container of the same image:
#   PG_IMAGE       the chart's postgres.image (repository:tag)
#   PG_CONTAINER   the PostgreSQL container's name or ID (for docker exec, like kubectl exec into the database's pod)
#   PGPASSWORD     the password of PGUSER (default pspsps), which owns PGDATABASE (default pspsps), on localhost:5432
# The full-volume case mounts a small tmpfs, so it needs sudo.
set -euo pipefail

: "${PG_IMAGE:?PG_IMAGE is not set}"
: "${PG_CONTAINER:?PG_CONTAINER is not set}"
: "${PGPASSWORD:?PGPASSWORD is not set}"
user="${PGUSER:-pspsps}"
db="${PGDATABASE:-pspsps}"
root="$(cd "$(dirname "$0")/../.." && pwd)"

backups="$(mktemp -d)"
small=""
cleanup() {
  docker rm -f backup-term > /dev/null 2>&1 || true
  if [ -n "$small" ]; then sudo umount "$small" 2>/dev/null || true; rmdir "$small" 2>/dev/null || true; fi
  rm -rf "$backups" 2>/dev/null || sudo rm -rf "$backups"
}
trap cleanup EXIT
# The pod's fsGroup makes the volume writable by uid 70; here, anyone may write.
chmod 0777 "$backups"

fail() { echo "FAIL: $*" >&2; exit 1; }

# psql inside the database's container, over its socket: what kubectl exec does.
sql() { docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -qtA -U "$user" -d "${2:-$db}" -c "$1"; }
pg() { docker exec -i "$PG_CONTAINER" "$@"; }

# The backup container's options; BACKUP_VOLUME (default: $backups) is mounted as /backups.
backup_args() { # backup_args <keep> [password]
  echo --network host --user 70:70 --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges \
    -v "$root/charts/pspsps/files/backup.sh:/scripts/backup.sh:ro" -v "${BACKUP_VOLUME:-$backups}:/backups" \
    -e PGHOST=localhost -e PGPORT=5432 -e PGUSER="$user" -e PGDATABASE="$db" -e "PGPASSWORD=${2:-$PGPASSWORD}" \
    -e BACKUP_DIR=/backups -e "BACKUP_KEEP=$1" -e BACKUP_STALE_SECONDS=3600
}
# The paths and values here have no spaces, so word splitting of backup_args is safe.
# shellcheck disable=SC2046
backup() { docker run --rm $(backup_args "$@") "$PG_IMAGE" /bin/sh /scripts/backup.sh; }

dumps() { find "${1:-$backups}" -maxdepth 1 -name 'pspsps-*.dump' -printf '%f\n' | sort; }
count() { dumps "$@" | wc -l; }
partials() { find "${1:-$backups}" -maxdepth 1 -name '*.partial' | wc -l; }

echo "== Some data, and the migrations so far"
sql "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" text PRIMARY KEY, \"ProductVersion\" text NOT NULL);"
sql "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260101000000_Initial', '10.0.0');"
sql "CREATE TABLE backup_test (id int PRIMARY KEY, name text NOT NULL);"
sql "INSERT INTO backup_test SELECT i, 'cat ' || i FROM generate_series(1, 100) i;"

echo "== Runs keeping two"
backup 2
first="$(dumps | head -n 1)"
sleep 1 # dumps are named to the second
backup 2
[ "$(count)" -eq 2 ] || fail "expected 2 dumps, got: $(dumps | tr '\n' ' ')"

echo "== Retention runs after the dump succeeded"
sleep 1
out="$(backup 2 2>&1)"
echo "$out"
[ "$(count)" -eq 2 ] || fail "expected 2 dumps, got: $(dumps | tr '\n' ' ')"
dumps | grep -qx "$first" && fail "the oldest dump ($first) was not deleted"
wrote="$(grep -n '^Wrote ' <<< "$out" | cut -d: -f1)"
deleted="$(grep -n "^Deleted $first" <<< "$out" | cut -d: -f1)"
[ -n "$wrote" ] && [ -n "$deleted" ] && [ "$wrote" -lt "$deleted" ] || fail "retention ran before the dump was written"
before="$(dumps)"

echo "== A run that can't reach the database deletes nothing"
sleep 1
if backup 2 wrong-password; then fail "a run with a wrong password succeeded"; fi
[ "$(dumps)" = "$before" ] || fail "a failed run changed the dumps: $(dumps | tr '\n' ' ')"
[ "$(partials)" -eq 0 ] || fail "a failed run left a .partial"

echo "== Another run's .partial survives; a stale one is cleared"
fresh="pspsps-20991231T235959Z.dump.other-pod.1.partial"
stale="pspsps-20000101T000000Z.dump.dead-pod.1.partial"
touch "$backups/$fresh"
touch -d '2 hours ago' "$backups/$stale"
sleep 1
backup 2
[ -e "$backups/$fresh" ] || fail "a run deleted another run's in-progress $fresh"
[ ! -e "$backups/$stale" ] || fail "a run left the stale $stale"
[ "$(partials)" -eq 1 ] || fail "a run left its own .partial"
rm -f "$backups/$fresh"
newest="$(dumps | tail -n 1)"

echo "== Bad BACKUP_KEEP is refused before anything is deleted"
for bad in 0 1 00 01 08 -1 x ""; do
  if backup "$bad"; then fail "BACKUP_KEEP='$bad' was accepted"; fi
  [ "$(count)" -eq 2 ] || fail "BACKUP_KEEP='$bad' changed the dumps: $(dumps | tr '\n' ' ')"
  [ "$(dumps | tail -n 1)" = "$newest" ] || fail "BACKUP_KEEP='$bad' wrote a dump"
done

echo "== TERM during a dump (the deadline, a deleted pod) stops it and leaves no .partial"
# pg_dump waits for its table locks: hold one, so the dump is still going when TERM arrives.
docker exec -d "$PG_CONTAINER" psql -U "$user" -d "$db" -c \
  "BEGIN; LOCK TABLE backup_test IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(120) AS backup_test_lock;"
# shellcheck disable=SC2046
docker run -d --name backup-term $(backup_args 2) "$PG_IMAGE" /bin/sh /scripts/backup.sh > /dev/null
timeout 30 bash -c "until [ \"\$(find '$backups' -maxdepth 1 -name '*.partial' | wc -l)\" -eq 1 ]; do sleep 0.2; done" \
  || fail "the dump never started"
sleep 1
docker kill --signal TERM backup-term > /dev/null
status="$(timeout 20 docker wait backup-term)" || fail "the run ignored TERM"
docker logs backup-term
[ "$status" -ne 0 ] || fail "a run stopped by TERM exited 0"
[ "$(partials)" -eq 0 ] || fail "a run stopped by TERM left its .partial"
[ "$(dumps | tail -n 1)" = "$newest" ] && [ "$(count)" -eq 2 ] || fail "a run stopped by TERM changed the dumps"
docker rm backup-term > /dev/null
sql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE query LIKE '%backup_test_lock%' AND pid <> pg_backend_pid();" > /dev/null

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
# docs/backups.md, "Restoring": the database dropped and created again empty, then the dump into it.
pg dropdb -U "$user" "$db"
pg createdb -U "$user" -O "$user" "$db"
dump | pg pg_restore -U "$user" -d "$db" --no-owner --single-transaction --exit-on-error
[ "$(sql "SELECT count(*), sum(id) FROM backup_test;")" = "100|5050" ] || fail "the restore did not bring the rows back"
[ -z "$(sql "SELECT to_regclass('public.newer');")" ] || fail "the newer table survived the restore"
[ "$(sql "SELECT string_agg(\"MigrationId\", ',') FROM \"__EFMigrationsHistory\";")" = "20260101000000_Initial" ] \
  || fail "the migration history is not the dump's"
[ "$(sql "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '$db';")" = "$user" ] \
  || fail "the restored database is not owned by $user"

echo "== A full volume: the run deletes the oldest dump once and tries again"
# Hard-to-compress data, so a dump is big enough that page rounding doesn't matter.
sql "CREATE TABLE filler AS SELECT i, md5(i::text) || md5((i * 7)::text) AS a FROM generate_series(1, 20000) i;"
backup 3 # sizes one dump
size="$(stat -c %s "$backups/$(dumps | tail -n 1)")"
small="$(mktemp -d)"
# Room for two dumps and a half: the third doesn't fit.
sudo mount -t tmpfs -o "size=$((size * 5 / 2)),mode=0777" tmpfs "$small"
export BACKUP_VOLUME="$small"
backup 3
sleep 1
backup 3
[ "$(count "$small")" -eq 2 ] || fail "expected 2 dumps on the small volume, got: $(dumps "$small" | tr '\n' ' ')"
oldest="$(dumps "$small" | head -n 1)"
sleep 1
out="$(backup 3 2>&1)" || { echo "$out"; fail "a run on a full volume failed"; }
echo "$out"
grep -q 'No space left on device' <<< "$out" || fail "the volume was not full: the test proves nothing"
[ "$(count "$small")" -eq 2 ] || fail "expected 2 dumps after the retry, got: $(dumps "$small" | tr '\n' ' ')"
dumps "$small" | grep -qx "$oldest" && fail "the oldest dump ($oldest) is still there"
[ "$(partials "$small")" -eq 0 ] || fail "the full-volume run left a .partial"

echo "== A full volume with one dump keeps it"
sudo rm -f "$small"/pspsps-*.dump
# Room for one dump and a half: the only dump stays, and the run fails.
sudo mount -o "remount,size=$((size * 3 / 2))" "$small"
backup 3
only="$(dumps "$small")"
sleep 1
if backup 3; then fail "a second dump fitted: the test proves nothing"; fi
unset BACKUP_VOLUME
[ "$(dumps "$small")" = "$only" ] || fail "a full-volume run deleted the only dump"
[ "$(partials "$small")" -eq 0 ] || fail "the failed run left a .partial"

echo "OK: dumps, retention, partial files, failed and stopped runs, full volumes and both restores work."
