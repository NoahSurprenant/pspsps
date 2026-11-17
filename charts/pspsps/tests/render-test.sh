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

if (( failures > 0 )); then
  exit 1
fi
echo "chart render checks passed"
