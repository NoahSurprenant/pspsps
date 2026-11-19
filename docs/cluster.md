# Cluster mode: several server pods

By default the chart runs one server pod: one Orleans silo, alone, replaced all at once on an update (`Recreate`). With
`cluster.enabled` it runs `cluster.replicas` pods that make one game: their silos join through Redis, SignalR's Redis
backplane carries each pod's messages to the others' connections, and a rolling update replaces them one at a time while
the game goes on. [architecture.md](architecture.md) has how the game spreads over the pods; this page is what a
deployment needs.

## Turning it on

```yaml
cluster:
  enabled: true
  replicas: 2              # at least 2
  kubeApi:
    cidr: 192.0.2.10/32    # the API server's address behind the kubernetes Service (below)
    port: 6443
redis:
  persistence:
    storageClass: ""       # a class whose volumes survive the Redis pod moving (see Redis, below)
    size: 1Gi
```

The chart then adds:

- **Redis** (a Deployment with one pod, `Recreate`, on its own PersistentVolumeClaim): Orleans' membership table, the
  SignalR backplane, and the moderation signal (below). It writes an append-only file (`appendfsync everysec`) to its
  volume, so the membership table survives a restart.
- **Orleans' Kubernetes hosting** (`Cluster__Mode=Kubernetes`): each silo takes its name and address from its pod
  and its service and cluster IDs from the pod's `orleans/serviceId` and `orleans/clusterId` labels (`orleans.*` values;
  keep them fixed, so a rolling update joins the running cluster), patches those labels onto its pod, watches the pods,
  and declares dead the silo of a pod that is deleted. The server's ServiceAccount gets a Role for that: `pods` get,
  list, watch, patch and delete, and nothing else; its token is mounted only in cluster mode.
- **The NetworkPolicy**: the pods reach each other on Orleans' ports (11111, 30000), Redis, and the API server at
  `cluster.kubeApi.cidr` and `.port`.
- **A rolling update** with `maxSurge: 1` and `maxUnavailable: 0`: a new pod starts, is ready once its silo is active in
  the cluster (`/health/ready` checks the database and the silo's membership), and only then does an old one stop.
- **A PodDisruptionBudget** of one pod down at a time, for node drains.
- **A 5 second `preStop` wait** (`cluster.preStopSeconds`): a stopping pod is out of the Service's endpoints before the
  server starts shutting down. Then the host's shutdown (45 s, `DOTNET_SHUTDOWNTIMEOUTSECONDS`) closes the hub's
  connections, which the browsers reconnect to another pod (SignalR tells them they may), and the silo leaves the
  cluster, handing its grains on. `terminationGracePeriodSeconds` (60) covers both.
- **Spread over the nodes** where it can (`cluster.topologySpread`, a `ScheduleAnyway` preference).

### The API server's address

NetworkPolicies match addresses after a Service's address is translated to its endpoints, so `cluster.kubeApi` is the
API server itself, not the `kubernetes` Service's cluster IP. Read it from the cluster:

```sh
kubectl get endpointslices -n default -l kubernetes.io/service-name=kubernetes \
  -o jsonpath='{.items[0].endpoints[*].addresses[*]} {.items[0].ports[0].port}'
```

With several control-plane nodes, give a CIDR that covers them all (or one address each, by editing the policy).
Without it the silos can't watch the pods: the server logs "Unable to monitor pods due to insufficient permissions" or
fails to reach the API server, and dead pods' silos are only found by Orleans' own probing (slower).

### The proxy in front: no sticky sessions needed

The browser connects to the hub with a WebSocket straight away, skipping SignalR's negotiate request
(`client/src/net.ts`, `hubTransports`). A connection is then one TCP connection to one pod, so the ingress can spread
connections over the pods any way it likes. ASP.NET Core's documentation, [SignalR hosting and
scaling](https://learn.microsoft.com/aspnet/core/signalr/scale), says sticky sessions are needed with several servers,
the Redis backplane included, except when every client uses WebSockets only with SkipNegotiation.

The proxy must pass WebSockets (Traefik and ingress-nginx do by default) and keep idle ones open for at least SignalR's
keep-alive (15 s).

A browser that can't open a WebSocket (a proxy on its side that strips them) falls back once to SignalR's negotiation,
with every transport. With several pods that fallback works only with sticky sessions at the ingress:
`ingressRoute.sticky: true` adds a Traefik cookie (`pspsps-pod`); for a plain Ingress, use the controller's own
affinity annotations. Without them such a browser can't connect while there are several pods (it could before, with
one pod).

### Redis

Orleans needs its membership table to last: a silo that finds the table gone fails ("The Redis membership table version
is missing"), and the cluster has to start over. Redis keeps it in an append-only file on its claim, so:

- Use a storage class whose volumes follow the Redis pod to another node (network storage), or pin Redis to its node,
  or accept that losing that node means restarting the server pods once Redis is back on an empty volume.
- A Redis restart (an update, a node drain) pauses what goes through it for a few seconds: the backplane (moves and chat
  between players on different pods) and new silos joining. The silos keep running and catch up.
- Its default limits (128Mi) are plenty: the table has a row per silo, and the backplane keeps nothing.

## What runs where, with several pods

- **Grains** (worlds, rooms, players' sessions) exist once in the cluster, on whichever pod Orleans placed them. Their
  timers (fishing bites, lease checks) run with them. A pod's grains move when it stops; a room's state is in memory, so
  a room that moves starts empty. A room whose pod is stopping tells its tabs, on every pod, that it is lost
  (`RoomLost`), and they walk back in at once (in CI's rolling update the room worked both ways again about 5 s after
  the old pod was stopped); a room whose pod dies outright is found lost at its tabs' next lease renewal, within 30 s
  (architecture.md, "Worlds, rooms and actors"). What a room only kept in memory goes with it: recent chat, a line in
  the pond, emote poses.
- **Connections** stay on the pod they opened on. Everything sent to a tab goes through SignalR's connection, user
  (player) or group (room) addresses, which the backplane delivers on whichever pod holds the connection.
- **A ban** reaches every pod: the pod that decides it publishes a signal on Redis (`pspsps:moderation`); each pod drops
  what it knew of the player's standing (so their next call is refused) and closes the player's connections it holds.
  Redis pub/sub delivers at most once: a pod cut off from Redis at that moment falls back on its standing cache, which
  rereads a player's standing at most 5 seconds old, and refuses (and closes) their next call.
- **Per pod, by design**: each pod's standing cache (5 s), its record of its own connections (`SessionHeartbeat`), the
  browser telemetry's rate limit (60 batches a minute per player on each pod) and its queue to the collector (a pod
  that stops drops what it hasn't sent), and the chat filter's word lists (each pod rereads its mounted ConfigMap).

## Rolling updates and versions

During a rolling update, old and new pods run side by side, and a grain call from one may run on the other. Grain
interfaces are versioned (`[Version]`), with Orleans' rolling upgrade settings, `BackwardCompatible` and
`AllCompatibleVersions` ([Grain interface versioning](https://learn.microsoft.com/dotnet/orleans/grains/grain-versioning/grain-versioning),
[Deploy new versions of grains](https://learn.microsoft.com/dotnet/orleans/grains/grain-versioning/deploying-new-versions-of-grains)):
an old pod's call may run on a new activation, and a new pod's calls only reach new ones. That holds only if every
change is backward compatible: architecture.md, "Changing what pods say to each other", has the rules, and a test
holds them.

The first switch from one pod to cluster mode runs the old single silo and the new cluster side by side for a moment
(the single pod has no Redis, so it isn't in their cluster): players on the old pod see only each other until it stops,
then reconnect into the cluster.

## Outside Kubernetes

`Cluster:Mode` picks how the silos find each other: `Localhost` (one silo; the default without
`ConnectionStrings:Redis`), `Redis` (silos joined through Redis, anywhere: their addresses are the hosts' own) or
`Kubernetes` (the default in a pod with Redis). Outside Kubernetes, `Orleans:ServiceId` and `Orleans:ClusterId` (both
`pspsps` by default) name the cluster. CI's two-pod job runs the image that way (`e2e/cluster/pods.sh`).

## What CI proves

- **Two pods end-to-end** (`e2e/cluster/cluster.spec.ts`, plain containers, Redis clustering): two players on different
  pods see each other walk and chat; a ban decided on one pod closes a tab's connection on the other (a tab that ignores
  being told) and takes both banned cats out of the room; a rolling update (a new pod, the "Service" switched to it, the
  old pod stopped with SIGTERM) brings the moved player back into the room with the other, who never dropped.
- **Cluster mode in Kubernetes** (`tests/chart/kind-test.sh`, kind): the chart installs with two replicas and both silos
  join; the ServiceAccount may do exactly what the hosting needs and the logs show no permission or API server errors,
  with the NetworkPolicy in force (kind's network plugin enforces NetworkPolicies, which the job checks too); a pod
  killed outright is declared dead by the hosting (it watches the pods) and replaced, and the cluster is two active
  silos again; Redis restarted keeps the membership table, and the silos carry on without restarting.

Not proven: a rolling update between two different versions of the code (CI runs one image), and a real cloud load
balancer or ingress controller in front (the two-pod job uses a TCP proxy).
