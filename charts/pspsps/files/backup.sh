#!/bin/sh
# Dumps the database (pg_dump's custom format, for pg_restore) into BACKUP_DIR, then deletes the oldest dumps beyond
# BACKUP_KEEP. The connection comes from libpq's PGHOST, PGPORT, PGUSER, PGPASSWORD and PGDATABASE.
# The chart's backup CronJob runs it; CI runs it against a real PostgreSQL (tests/chart/backup-test.sh).
set -eu

dir="${BACKUP_DIR:?BACKUP_DIR is not set}"
keep="${BACKUP_KEEP:?BACKUP_KEEP is not set}"
case "$keep" in
  '' | *[!0-9]* | 0)
    echo "BACKUP_KEEP must be a whole number of at least 1, not '$keep'." >&2
    exit 1
    ;;
esac

# Dumps are named by UTC time, so name order is age order.
name="pspsps-$(date -u +%Y%m%dT%H%M%SZ).dump"

# Into a .partial file first, so a failed dump never counts as a backup or pushes a good one out. A run that died
# leaves one behind: clear those first.
rm -f "$dir"/*.partial
pg_dump --format=custom --file="$dir/$name.partial"
mv "$dir/$name.partial" "$dir/$name"
echo "Wrote $name ($(wc -c < "$dir/$name") bytes)."

# Keep the newest BACKUP_KEEP.
ls -1 "$dir" | grep -E '^pspsps-[0-9]{8}T[0-9]{6}Z\.dump$' | sort -r | tail -n +"$((keep + 1))" | while read -r old; do
  rm -f "$dir/$old"
  echo "Deleted $old."
done
