#!/usr/bin/env bash
# The chart in cluster mode in a real, throwaway Kubernetes cluster (CI's "Cluster mode in Kubernetes" job, on kind):
# two server pods, Redis and PostgreSQL as the chart makes them, its RBAC and NetworkPolicies in force. Checks that
#   - both silos join the cluster (two active silos in Orleans' membership table, in Redis),
#   - Orleans' Kubernetes hosting may read and watch the pods (RBAC) and reach the API server (NetworkPolicy),
#   - a pod killed outright is declared dead by the hosting and replaced, and the cluster is two active silos again,
#   - Redis restarted keeps the membership table (its append-only file, on its volume), and the silos carry on,
#   - the NetworkPolicies are enforced (else the API server check above proves nothing).
# Needs kubectl and helm on a cluster with the image loaded:  kind-test.sh <image repository> <image tag>
set -euo pipefail

repository=$1 tag=$2
chart="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../charts/pspsps" && pwd)"
ns=pspsps
k() { kubectl -n "$ns" "$@"; }
step() { echo "::group::$*"; }
done_step() { echo "::endgroup::"; }
fail() { echo "::error::$*" >&2; exit 1; }

# The silos in Orleans' membership table, one "<status> <silo name>" line each (3 active, 6 dead).
members() {
  k exec deploy/pspsps-redis -- redis-cli --raw HVALS pspsps/members/pspsps \
    | jq -Rr 'fromjson? | objects | "\(.Status) \(.SiloName)"'
}
active() { members | grep -c '^3 ' || true; }

# Waits up to <seconds> for <command> to succeed.
until_ok() {
  local seconds=$1
  shift
  local deadline=$((SECONDS + seconds))
  until "$@"; do
    (( SECONDS < deadline )) || return 1
    sleep 2
  done
}
two_active() { [[ "$(active)" == 2 ]]; }
server_restarts() {
  k get pods -l app.kubernetes.io/component=server \
    -o jsonpath='{range .items[*]}{.status.containerStatuses[0].restartCount}{"\n"}{end}' | awk '{ s += $1 } END { print s + 0 }'
}
servers_ready() {
  [[ "$(k get pods -l app.kubernetes.io/component=server \
    -o jsonpath='{range .items[*]}{.metadata.deletionTimestamp}{.status.conditions[?(@.type=="Ready")].status}{"\n"}{end}' \
    | grep -cx True)" == 2 ]]
}

step "Install the chart, cluster mode, two pods"
kubectl create namespace "$ns"
k create secret generic pspsps --from-literal=POSTGRES_PASSWORD=kindtest
# The API server as the pods reach it once the kubernetes Service's address is translated: what the NetworkPolicy opens.
api=$(kubectl get endpointslices -n default -l kubernetes.io/service-name=kubernetes \
  -o jsonpath='{.items[0].endpoints[0].addresses[0]}')
api_port=$(kubectl get endpointslices -n default -l kubernetes.io/service-name=kubernetes \
  -o jsonpath='{.items[0].ports[0].port}')
echo "API server: $api:$api_port"
helm install pspsps "$chart" -n "$ns" --wait --timeout 8m \
  --set cluster.enabled=true --set cluster.replicas=2 \
  --set cluster.kubeApi.cidr="$api/32" --set cluster.kubeApi.port="$api_port" \
  --set image.repository="$repository" --set image.tag="$tag" --set image.pullPolicy=Never \
  --set server.resources.requests.cpu=50m \
  --set-string 'extraEnv[0].name=Serilog__MinimumLevel__Override__Orleans.Hosting.Kubernetes' \
  --set-string 'extraEnv[0].value=Information'
k get pods -o wide
done_step

step "Both silos joined"
until_ok 120 two_active || { members; fail "not two active silos in Orleans' membership table"; }
members
done_step

step "Kubernetes hosting: RBAC and the API server"
sa="system:serviceaccount:$ns:pspsps"
for verb in get list watch patch delete; do
  [[ "$(kubectl auth can-i "$verb" pods -n "$ns" --as "$sa")" == yes ]] || fail "the server may not $verb pods"
done
[[ "$(kubectl auth can-i list secrets -n "$ns" --as "$sa")" == no ]] || fail "the server may read secrets"
# No permission errors and no failures reaching the API server (the agent logs both, at Error).
logs=$(k logs -l app.kubernetes.io/component=server --tail=-1 --prefix)
if grep -E 'insufficient permissions|Forbidden|Error while initializing Kubernetes cluster agent|Error monitoring Kubernetes pods' <<< "$logs"; then
  fail "Orleans' Kubernetes hosting can't do its job (above)"
fi
done_step

step "A pod killed outright: declared dead, replaced, two active silos again"
victim=$(k get pods -l app.kubernetes.io/component=server -o jsonpath='{.items[0].metadata.name}')
survivor=$(k get pods -l app.kubernetes.io/component=server -o jsonpath='{.items[1].metadata.name}')
echo "killing $victim"
k delete pod "$victim" --grace-period=0 --force --wait=false
# The log line is rendered JSON (the pod name in quotes).
declared_dead() { k logs "$survivor" | grep -q "dead since its corresponding pod, .*$victim.*, has been deleted"; }
victim_dead() { members | grep -qx "6 $victim"; }
until_ok 60 declared_dead || fail "$survivor's Kubernetes hosting didn't declare $victim's silo dead (it watches the pods)"
until_ok 90 victim_dead || { members; fail "$victim's silo isn't dead in the membership table"; }
until_ok 240 servers_ready || { k get pods; fail "the Deployment didn't bring back two ready pods"; }
until_ok 120 two_active || { members; fail "not two active silos after the kill"; }
members
done_step

step "Redis restarted: the membership table survives, the silos carry on"
before=$(members | wc -l)
restarts_before=$(server_restarts)
redis_pod=$(k get pods -l app.kubernetes.io/component=redis -o jsonpath='{.items[0].metadata.name}')
k delete pod "$redis_pod" --wait=true
k rollout status deploy/pspsps-redis --timeout=180s
# Read before the silos write again: what Redis loaded from its append-only file.
version=$(k exec deploy/pspsps-redis -- redis-cli --raw HGET pspsps/members/pspsps Version)
after=$(members | wc -l)
echo "membership rows: $before before, $after after; table version: $version"
[[ -n "$version" ]] || fail "the membership table's version is gone after the restart (Orleans can't read the table)"
(( after >= before )) || fail "membership rows lost in the restart"
until_ok 120 two_active || { members; fail "not two active silos after Redis restarted"; }
restarts=$(server_restarts)
echo "server container restarts: $restarts_before before, $restarts after"
(( restarts == restarts_before )) || fail "a server restarted while Redis did"
until_ok 60 servers_ready || { k get pods; fail "a server pod isn't ready after Redis restarted"; }
done_step

step "The game answers through the Service"
k port-forward svc/pspsps 8080:80 > /dev/null &
forward=$!
trap 'kill $forward 2> /dev/null || true' EXIT
until_ok 30 curl -fs http://localhost:8080/health/ready > /dev/null || fail "the Service isn't ready"
curl -fs -X POST http://localhost:8080/auth/guest | jq -e .id > /dev/null || fail "no guest through the Service"
done_step

step "The NetworkPolicies are enforced"
# A pod the policies don't name can't reach Redis: so the API server check above ran under the server's policy.
if k run np-probe --image=docker.io/library/redis:8-alpine --restart=Never --rm -i --quiet --command -- \
  timeout 10 redis-cli -h pspsps-redis -p 6379 ping 2> /dev/null | grep -q PONG; then
  fail "a pod outside the policies reached Redis: NetworkPolicies aren't enforced in this cluster"
fi
done_step

echo "cluster mode works in Kubernetes"
