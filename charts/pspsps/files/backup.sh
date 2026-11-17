#!/bin/sh
# Dumps the database (pg_dump's custom format, for pg_restore) into BACKUP_DIR, keeping the newest BACKUP_KEEP dumps.
# The connection comes from libpq's PGHOST, PGPORT, PGUSER, PGPASSWORD and PGDATABASE.
# BACKUP_STALE_SECONDS (optional): a .partial file older than this is from a run that died, and is deleted; the chart
# passes the job's activeDeadlineSeconds, past which no run is still going. Unset: none are deleted.
# The chart's backup CronJob runs it; CI runs it against a real PostgreSQL (tests/chart/backup-test.sh).
set -eu

dir="${BACKUP_DIR:?BACKUP_DIR is not set}"
keep="${BACKUP_KEEP:-}"
stale="${BACKUP_STALE_SECONDS:-}"

# Whole numbers of at least 1 with no leading zero (which shell arithmetic would read as octal).
whole() {
  case "$1" in
    '' | 0* | *[!0-9]*) return 1 ;;
  esac
}
whole "$keep" || { echo "BACKUP_KEEP must be a whole number of at least 1, not '$keep'." >&2; exit 1; }
if [ -n "$stale" ] && ! whole "$stale"; then
  echo "BACKUP_STALE_SECONDS must be a whole number of seconds, not '$stale'." >&2
  exit 1
fi

# Dumps are named by UTC time, so name order is age order.
dumps() { ls -1 "$dir" | grep -E '^pspsps-[0-9]{8}T[0-9]{6}Z\.dump$' | sort -r || true; }

if [ -n "$stale" ]; then
  find "$dir" -maxdepth 1 -name 'pspsps-*.partial' -mmin +"$(( (stale + 59) / 60 ))" | while read -r old; do
    rm -f "$old"
    echo "Deleted ${old##*/}, left by a run that died."
  done
fi

# Make room first: keep - 1 old dumps, so the new one fits even when a full volume made the last runs fail.
dumps | tail -n +"$keep" | while read -r old; do
  rm -f "$dir/$old"
  echo "Deleted $old."
done

name="pspsps-$(date -u +%Y%m%dT%H%M%SZ).dump"
# Into this run's own .partial file (another run may be writing its own), so a failed dump never counts as a backup.
partial="$dir/$name.$(hostname).$$.partial"
trap 'rm -f "$partial"' EXIT
pg_dump --format=custom --file="$partial"
mv "$partial" "$dir/$name"
echo "Wrote $name ($(wc -c < "$dir/$name") bytes)."
