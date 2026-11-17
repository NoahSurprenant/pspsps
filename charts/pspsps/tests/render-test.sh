#!/usr/bin/env bash
# Checks on the rendered chart that helm lint can't make. Run from the repository root.
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

# An env var's value in a rendered container: the "value:" line after "- name: <var>".
env_value() {
  { grep -A1 -E "^ +- name: $2$" <<< "$1" || true; } | sed -n 's/^ *value: *"\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1
}

# PostgreSQL's own tools (the probes' pg_isready, psql through kubectl exec) connect as the app's role and database:
# the OS user, postgres, has no role, and every connection as it logs a FATAL.
for values in "" "--set postgres.username=cats --set postgres.database=game"; do
  # shellcheck disable=SC2086
  render=$(helm template pspsps "$chart" $values)
  postgres=$(document "$render" Deployment pspsps-postgres)
  user=$(env_value "$postgres" PGUSER)
  database=$(env_value "$postgres" PGDATABASE)
  expected_user=pspsps expected_database=pspsps
  if [[ -n "$values" ]]; then expected_user=cats expected_database=game; fi
  [[ "$user" == "$expected_user" ]] || fail "PGUSER is '$user', want '$expected_user' (${values:-defaults})"
  [[ "$database" == "$expected_database" ]] || fail "PGDATABASE is '$database', want '$expected_database' (${values:-defaults})"
  if grep -q -- '"-U"\|"-d"' <<< "$postgres"; then
    fail "the probes pass -U or -d; they read PGUSER and PGDATABASE (${values:-defaults})"
  fi
done

if (( failures > 0 )); then
  exit 1
fi
echo "chart render checks passed"
