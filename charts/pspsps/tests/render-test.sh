#!/usr/bin/env bash
# Checks on the rendered chart that helm lint can't make. Runs from anywhere; needs only helm.
set -euo pipefail

chart="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
failures=0

fail() {
  echo "FAIL: $*" >&2
  failures=$((failures + 1))
}

# An env var's literal value in a rendered container: the "value:" line right after "- name: <var>".
env_value() {
  { grep -A1 -E "^ +- name: $2$" <<< "$1" || true; } | sed -n 's/^ *value: *"\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1
}

# PostgreSQL's own tools (the probes' pg_isready, psql through kubectl exec) connect as the app's role and database:
# the OS user, postgres, has no role, and every connection as it logs a FATAL. The values are set here, not taken from
# the chart's defaults, so the test checks that the env follows them.
probe='["pg_isready", "-q", "-h", "127.0.0.1", "-p", "5432"]'
for names in "pspsps pspsps" "cats game"; do
  read -r user database <<< "$names"
  postgres=$(helm template pspsps "$chart" --show-only templates/postgres.yaml \
    --set "postgres.username=$user" --set "postgres.database=$database")

  [[ "$(env_value "$postgres" PGUSER)" == "$user" ]] || fail "PGUSER isn't '$user'"
  [[ "$(env_value "$postgres" PGDATABASE)" == "$database" ]] || fail "PGDATABASE isn't '$database'"

  # Every probe runs exactly this command: any user, database or other connection flag added later fails here.
  commands=$(grep -E '^ +command:' <<< "$postgres" | sed 's/^ *command: *//')
  [[ $(grep -c . <<< "$commands") -eq 3 ]] || fail "expected 3 probe commands, got: $commands"
  while IFS= read -r command; do
    [[ "$command" == "$probe" ]] || fail "a probe runs $command, not $probe"
  done <<< "$commands"
done

# One document of a kind from a render.
document() {
  awk -v kind="$2" '
    /^---/ { if (found) print doc; doc = ""; found = 0; next }
    { doc = doc $0 "\n" }
    $0 == "kind: " kind { found = 1 }
    END { if (found) print doc }
  ' <<< "$1"
}

# A pod's securityContext (the one with runAsNonRoot, not a container's), unindented.
pod_security_context() {
  awk '
    BEGIN { base = -1 }
    base >= 0 { match($0, /^ */); if (RLENGTH > base && $0 !~ /^ *$/) { print substr($0, base + 1); next } base = -1 }
    /^ *securityContext:$/ { match($0, /^ */); start = RLENGTH; getline next_line
      if (next_line ~ /runAsNonRoot/) { base = start; match(next_line, /^ */); print substr(next_line, base + 1) } }
  ' <<< "$1"
}

backup() { helm template pspsps "$chart" --show-only templates/backup.yaml --set backup.enabled=true "$@"; }

# Backups: off by default.
if helm template pspsps "$chart" | grep -q 'component: backup'; then fail "backup objects render with backups off"; fi

# The chart's backup claim outlives an uninstall unless told otherwise.
pvc=$(document "$(backup)" PersistentVolumeClaim)
[[ -n "$pvc" ]] || fail "no backup PersistentVolumeClaim"
grep -q '^    helm.sh/resource-policy: keep$' <<< "$pvc" || fail "the backup PVC is not kept on uninstall"
pvc=$(document "$(backup --set backup.persistence.keepOnUninstall=false)" PersistentVolumeClaim)
[[ -n "$pvc" ]] || fail "no backup PersistentVolumeClaim with keepOnUninstall=false"
if grep -q 'helm.sh/resource-policy' <<< "$pvc"; then fail "keepOnUninstall=false still keeps the backup PVC"; fi

# An existing claim replaces the chart's own.
existing=$(backup --set backup.persistence.existingClaim=dumps)
[[ -z "$(document "$existing" PersistentVolumeClaim)" ]] || fail "existingClaim still makes a PVC"
grep -q '^ *claimName: dumps$' <<< "$existing" || fail "existingClaim is not mounted"

# Each run's pod mounts the ConfigMap as it is then: a checksum annotation only churns the CronJob.
cronjob=$(document "$(backup)" CronJob)
[[ -n "$cronjob" ]] || fail "no backup CronJob"
if grep -q 'checksum/' <<< "$cronjob"; then fail "the backup CronJob has a checksum annotation"; fi

# The backup pod runs as the database's pod does, and neither relabels its whole volume on every start.
postgres_sc=$(pod_security_context "$(helm template pspsps "$chart" --show-only templates/postgres.yaml)")
backup_sc=$(pod_security_context "$cronjob")
grep -qx 'fsGroupChangePolicy: OnRootMismatch' <<< "$postgres_sc" || fail "PostgreSQL's pod has no fsGroupChangePolicy"
[[ "$postgres_sc" == "$backup_sc" ]] || fail "the backup pod's securityContext isn't PostgreSQL's"

# The backup pod is placed like the server.
placed=$(backup -f /dev/stdin <<'YAML'
nodeSelector: { disk: ssd }
tolerations: [{ key: dedicated, operator: Exists, effect: NoSchedule }]
affinity: { nodeAffinity: { requiredDuringSchedulingIgnoredDuringExecution: { nodeSelectorTerms: [
  { matchExpressions: [{ key: zone, operator: In, values: [a] }] }] } } }
YAML
)
grep -qx '            disk: ssd' <<< "$placed" || fail "the backup pod ignores nodeSelector"
grep -q '^ *- effect: NoSchedule$' <<< "$placed" || fail "the backup pod ignores tolerations"
grep -qx '            nodeAffinity:' <<< "$placed" || fail "the backup pod ignores affinity"

# Settings the chart refuses: keep 1 leaves no dump while a run deletes the previous one.
for bad in backup.keep=1 backup.keep=0 backup.activeDeadlineSeconds=0 postgres.enabled=false; do
  if backup --set "$bad" > /dev/null 2>&1; then fail "rendered with backups on and $bad"; fi
done

if (( failures > 0 )); then
  exit 1
fi
echo "chart render checks passed"
