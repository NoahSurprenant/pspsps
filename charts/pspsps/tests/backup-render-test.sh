#!/usr/bin/env bash
# Checks on the rendered backup (backup.enabled) that helm lint can't make. Run from the repository root.
set -euo pipefail

chart=charts/pspsps
failures=0

fail() {
  echo "FAIL: $*" >&2
  failures=$((failures + 1))
}

# The document of one kind and name from a render.
document() {
  awk -v kind="$2" -v name="$3" '
    /^---/ { if (match_kind && match_name) print doc; doc = ""; match_kind = 0; match_name = 0; next }
    { doc = doc $0 "\n" }
    $0 == "kind: " kind { match_kind = 1 }
    $0 == "  name: " name { match_name = 1 }
    END { if (match_kind && match_name) print doc }
  ' <<< "$1"
}

# A pod's securityContext (the one with runAsNonRoot, not a container's), unindented.
pod_security_context() {
  awk '
    base >= 0 { match($0, /^ */); if (RLENGTH > base && $0 !~ /^ *$/) { print substr($0, base + 1); next } base = -1 }
    /^ *securityContext:$/ { match($0, /^ */); start = RLENGTH; getline next_line
      if (next_line ~ /runAsNonRoot/) { base = start; match(next_line, /^ */); print substr(next_line, base + 1) } }
    BEGIN { base = -1 }
  ' <<< "$1"
}

render=$(helm template pspsps "$chart" --set backup.enabled=true)
pvc=$(document "$render" PersistentVolumeClaim pspsps-backup)
cronjob=$(document "$render" CronJob pspsps-backup)
postgres=$(document "$render" Deployment pspsps-postgres)

[[ -n "$pvc" ]] || fail "no PersistentVolumeClaim pspsps-backup"
[[ -n "$cronjob" ]] || fail "no CronJob pspsps-backup"

# Uninstalling the chart must not take the dumps with the database.
grep -q '^    helm.sh/resource-policy: keep$' <<< "$pvc" || fail "the backup PVC is not kept on uninstall"
off=$(document "$(helm template pspsps "$chart" --set backup.enabled=true --set backup.persistence.keepOnUninstall=false)" \
  PersistentVolumeClaim pspsps-backup)
grep -q 'helm.sh/resource-policy' <<< "$off" && fail "keepOnUninstall=false still keeps the backup PVC"

# Each run's pod mounts the ConfigMap as it is then: a checksum annotation only churns the CronJob.
grep -q 'checksum/' <<< "$cronjob" && fail "the CronJob has a checksum annotation"

# The backup pod runs as the database's pod does, so it can read and write what the database's user owns.
pod_sc=$(pod_security_context "$postgres")
backup_sc=$(pod_security_context "$cronjob")
[[ -n "$pod_sc" ]] || fail "no pod securityContext on the database's pod"
[[ "$pod_sc" == "$backup_sc" ]] || fail "the backup pod's securityContext differs from the database's:
$pod_sc
---
$backup_sc"

# An existing claim replaces the chart's own.
existing=$(helm template pspsps "$chart" --set backup.enabled=true --set backup.persistence.existingClaim=dumps)
[[ -z "$(document "$existing" PersistentVolumeClaim pspsps-backup)" ]] || fail "existingClaim still makes a PVC"
grep -q '^ *claimName: dumps$' <<< "$(document "$existing" CronJob pspsps-backup)" || fail "existingClaim is not mounted"

# Settings the chart refuses.
for bad in "--set backup.keep=0" "--set postgres.enabled=false"; do
  # shellcheck disable=SC2086
  if helm template pspsps "$chart" --set backup.enabled=true $bad > /dev/null 2>&1; then
    fail "rendered with backup.enabled and $bad"
  fi
done

# Off by default.
grep -q 'component: backup' <<< "$(helm template pspsps "$chart")" && fail "backup objects render with backups off"

if (( failures > 0 )); then
  exit 1
fi
echo "backup render checks passed"
