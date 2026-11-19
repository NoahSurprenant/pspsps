#!/usr/bin/env bash
# Several server pods of one game as plain containers on one Docker network, for the two-pod end-to-end run
# (cluster.spec.ts): PostgreSQL, Redis, and pods from the same image, clustered through Redis (Cluster:Mode=Redis:
# Orleans' Kubernetes hosting needs Kubernetes). Each pod's HTTP port is published on the host.
#
#   pods.sh up                  PostgreSQL, Redis, pod-a on 8081 and pod-b on 8082; prints E2E_ADMIN_ID and
#                               E2E_ADMIN_COOKIE lines for $GITHUB_ENV (an admin guest made on pod-a)
#   pods.sh start <name> <port> another pod (needs E2E_ADMIN_ID), once it is ready
#   pods.sh stop <name>         stops a pod as Kubernetes does: SIGTERM, then up to 60 s to finish
#   pods.sh members             Orleans' membership table in Redis: one "<status> <silo>" line per silo
#   pods.sh logs                every pod's log
set -euo pipefail

image=${PSPSPS_IMAGE:-pspsps:e2e}
network=pspsps-cluster

ready() {
  timeout 120 bash -c "until curl -fs http://localhost:$1/health/ready > /dev/null; do sleep 1; done" \
    || { echo "the pod on port $1 isn't ready" >&2; return 1; }
}

start() {
  local name=$1 port=$2
  shift 2
  docker run -d --name "$name" --network "$network" -p "$port:8080" \
    -e ConnectionStrings__Pspsps="Host=postgres;Database=pspsps;Username=postgres;Password=e2e" \
    -e ConnectionStrings__Redis=redis:6379 \
    -e Cluster__Mode=Redis \
    -e DOTNET_SHUTDOWNTIMEOUTSECONDS=45 \
    "$@" "$image" > /dev/null
  ready "$port"
}

case "${1:-}" in
  up)
    docker network create "$network" > /dev/null
    docker run -d --name postgres --network "$network" -e POSTGRES_PASSWORD=e2e -e POSTGRES_DB=pspsps \
      postgres:18-alpine > /dev/null
    docker run -d --name redis --network "$network" redis:8-alpine \
      redis-server --appendonly yes --appendfsync everysec > /dev/null
    timeout 60 bash -c 'until docker exec postgres pg_isready -q -U postgres; do sleep 1; done'
    # The admin: a guest made on a first pod, then listed by player ID in every pod's settings (as production lists its
    # admins). Its cookie works on every pod: the key ring is in the database.
    start pod-a 8081 >&2
    headers=$(curl -fs -D - -o /dev/null -X POST http://localhost:8081/auth/guest)
    cookie=$(sed -n 's/^[Ss]et-[Cc]ookie: \(pspsps=[^;]*\).*/\1/p' <<< "$headers")
    id=$(curl -fs -H "Cookie: $cookie" http://localhost:8081/api/me | jq -r .id)
    test -n "$cookie" && test -n "$id"
    # Stopped as Kubernetes would (SIGTERM): its silo leaves the membership table as dead, not as a silo gone silent.
    docker stop -t 60 pod-a > /dev/null && docker rm pod-a > /dev/null
    E2E_ADMIN_ID=$id "$0" start pod-a 8081
    E2E_ADMIN_ID=$id "$0" start pod-b 8082
    echo "E2E_ADMIN_ID=$id"
    echo "E2E_ADMIN_COOKIE=$cookie"
    ;;
  start)
    start "$2" "$3" -e Moderation__AdminPlayerIds__0="${E2E_ADMIN_ID:?set E2E_ADMIN_ID to the admin player ID}"
    ;;
  stop)
    docker stop -t 60 "$2" > /dev/null
    ;;
  members)
    # Hash fields: the table's version, then one JSON entry per silo (Status: 3 active, 6 dead).
    docker exec redis redis-cli --raw HVALS pspsps/members/pspsps \
      | jq -Rr 'fromjson? | objects | "\(.Status) \(.SiloAddress // .SiloName // "?")"'
    ;;
  logs)
    for pod in $(docker ps -a --filter "network=$network" --filter "name=pod-" --format '{{.Names}}'); do
      echo "::group::$pod"
      docker logs "$pod" 2>&1 | tail -n 400
      echo "::endgroup::"
    done
    ;;
  *)
    echo "usage: pods.sh up | start <name> <port> | stop <name> | members | logs" >&2
    exit 2
    ;;
esac
