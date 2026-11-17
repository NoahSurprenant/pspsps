#!/bin/sh
# Dumps the database (pg_dump's custom format, for pg_restore) into BACKUP_DIR, keeping the newest BACKUP_KEEP dumps.
# The connection comes from libpq's PGHOST, PGPORT, PGUSER, PGPASSWORD and PGDATABASE.
# BACKUP_STALE_SECONDS (optional): a .partial file older than this is from a run that died, and is deleted; the chart
# passes the job's activeDeadlineSeconds, past which no run is still going. Unset: none are deleted.
# The chart's backup CronJob runs it as the container's PID 1; CI runs it against a real PostgreSQL
# (tests/chart/backup-test.sh).
#
# A failed run never costs a good dump: nothing is deleted until a new dump is complete, except when the volume is
# full - then the oldest dump goes (never the only one) and the dump is tried once more.
set -eu

dir="${BACKUP_DIR:?BACKUP_DIR is not set}"
keep="${BACKUP_KEEP:-}"
stale="${BACKUP_STALE_SECONDS:-}"

# Whole numbers with no leading zero (which shell arithmetic would read as octal).
whole() {
  case "$1" in
    '' | 0* | *[!0-9]*) return 1 ;;
  esac
}
# With 1, a new dump could only be made room for by deleting the only one.
if ! whole "$keep" || [ "$keep" -lt 2 ]; then
  echo "BACKUP_KEEP must be a whole number of at least 2, not '$keep'." >&2
  exit 1
fi
if [ -n "$stale" ] && ! whole "$stale"; then
  echo "BACKUP_STALE_SECONDS must be a whole number of seconds, not '$stale'." >&2
  exit 1
fi

# This run's files, removed however it ends; pg_dump stopped first when a signal ends it.
partial=""
child=""
errors="$(mktemp)"
cleanup() {
  if [ -n "$child" ]; then
    kill "$child" 2> /dev/null || true
    wait "$child" 2> /dev/null || true
  fi
  [ -z "$partial" ] || rm -f "$partial"
  rm -f "$errors"
}
trap cleanup EXIT
# PID 1 ignores signals it has no handler for: without these, the kubelet's TERM would wait out the grace period.
trap 'echo "Stopped by a signal." >&2; exit 143' TERM
trap 'echo "Stopped by a signal." >&2; exit 130' INT

# Before anything changes on the volume.
if ! psql -X -qtA -c 'select 1' > /dev/null; then
  echo "Can't reach the database: nothing changed." >&2
  exit 1
fi

# Dumps are named by UTC time, so name order is age order. Newest first.
dumps() { ls -1 "$dir" | grep -E '^pspsps-[0-9]{8}T[0-9]{6}Z\.dump$' | sort -r || true; }

if [ -n "$stale" ]; then
  find "$dir" -maxdepth 1 -name 'pspsps-*.partial' -mmin +"$(( (stale + 59) / 60 ))" | while read -r old; do
    rm -f "$old"
    echo "Deleted ${old##*/}, left by a run that died."
  done
fi

name="pspsps-$(date -u +%Y%m%dT%H%M%SZ).dump"
# This run's own .partial file (another run may be writing its own), so a failed dump never counts as a backup.
partial="$dir/$name.$(hostname).$$.partial"

# In the background, so a signal reaches the traps above while it runs.
dump() {
  pg_dump --format=custom --file="$partial" 2> "$errors" &
  child=$!
  status=0
  wait "$child" || status=$?
  child=""
  cat "$errors" >&2
  return "$status"
}

if ! dump; then
  rm -f "$partial"
  oldest="$(dumps | tail -n 1)"
  if ! grep -q 'No space left on device' "$errors"; then
    exit 1
  elif [ "$(dumps | wc -l)" -lt 2 ]; then
    echo "The volume is full, and its only dump stays: make it bigger." >&2
    exit 1
  fi
  rm -f "$dir/$oldest"
  echo "The volume is full: deleted the oldest dump, $oldest, and trying again."
  dump
fi
mv "$partial" "$dir/$name"
partial=""
echo "Wrote $name ($(wc -c < "$dir/$name") bytes)."

dumps | tail -n +"$((keep + 1))" | while read -r old; do
  rm -f "$dir/$old"
  echo "Deleted $old."
done
