# Backups

Players' data lives only in the chart's PostgreSQL. With `backup.enabled`, a CronJob dumps it on `backup.schedule`
with `pg_dump --format=custom` onto a volume (the chart's own PersistentVolumeClaim, or
`backup.persistence.existingClaim`) and keeps the newest `backup.keep` dumps. The script is
`charts/pspsps/files/backup.sh`; CI runs it and both restores below against a real PostgreSQL (`tests/chart/backup-test.sh`).

Dumps are named `pspsps-<UTC time>.dump`, e.g. `pspsps-20261117T030000Z.dump`. A run writes to a `.partial` file and
renames it when `pg_dump` succeeds, so a failed run never replaces a good dump. The job uses the database's own image,
so `pg_dump` is always the server's version.

The commands below assume the release is named `pspsps` in namespace `pspsps`; with another release name, the objects
are `<release>-pspsps-postgres` and so on (or `<release>-postgres` when the release name contains `pspsps`).

```sh
kubectl config set-context --current --namespace pspsps
```

## Taking one now

```sh
kubectl create job --from=cronjob/pspsps-backup pspsps-backup-manual
kubectl wait --for=condition=complete job/pspsps-backup-manual --timeout=10m
kubectl logs job/pspsps-backup-manual
kubectl delete job pspsps-backup-manual
```

## Getting a dump out

The dumps are on the backup volume, which only the backup job mounts. Start a pod that mounts it (same user and
security settings as the job, so a restricted namespace admits it), copy a dump out, and delete the pod. With
`backup.persistence.existingClaim`, put that claim's name in `claimName`.

```sh
kubectl apply -f - <<'YAML'
apiVersion: v1
kind: Pod
metadata:
  name: pspsps-backup-shell
spec:
  automountServiceAccountToken: false
  securityContext:
    runAsNonRoot: true
    runAsUser: 70
    runAsGroup: 70
    fsGroup: 70
    seccompProfile: { type: RuntimeDefault }
  containers:
    - name: shell
      image: docker.io/library/postgres:18-alpine
      command: ["sleep", "3600"]
      resources:
        requests: { cpu: 10m, memory: 16Mi }
        limits: { cpu: 100m, memory: 64Mi }
      securityContext:
        allowPrivilegeEscalation: false
        readOnlyRootFilesystem: true
        capabilities: { drop: [ALL] }
      volumeMounts:
        - { name: backups, mountPath: /backups, readOnly: true }
  volumes:
    - name: backups
      persistentVolumeClaim:
        claimName: pspsps-backup
YAML
kubectl wait --for=condition=ready pod/pspsps-backup-shell
kubectl exec pspsps-backup-shell -- ls -l /backups
kubectl exec pspsps-backup-shell -- cat /backups/pspsps-20261117T030000Z.dump > pspsps.dump
kubectl delete pod pspsps-backup-shell
```

A ReadWriteOnce volume can only be mounted on one node at a time: if the pod stays Pending while a backup runs, wait
for the run to finish.

## Trying a dump against a copy

Restore it into a scratch database next to the real one and look at it; nothing the game uses changes. `pg_restore`
runs inside the database's pod and connects over its socket, so it needs no password. `pspsps` after `-U` is
`postgres.username`.

```sh
kubectl exec deploy/pspsps-postgres -- createdb -U pspsps restore_test
kubectl exec -i deploy/pspsps-postgres -- pg_restore -U pspsps -d restore_test --no-owner --exit-on-error < pspsps.dump
kubectl exec -it deploy/pspsps-postgres -- psql -U pspsps -d restore_test   # look around, \q to leave
kubectl exec deploy/pspsps-postgres -- dropdb -U pspsps restore_test
```

## Restoring

This replaces the database's contents with the dump's: everything written since the dump is lost.

1. Stop the server, so nothing writes while the restore runs (and it doesn't serve half-restored data). Argo CD or
   another controller syncing the chart will scale it back up: pause its sync first.

   ```sh
   kubectl scale deploy/pspsps --replicas=0
   kubectl wait --for=delete pod -l app.kubernetes.io/instance=pspsps,app.kubernetes.io/component=server --timeout=2m
   ```

2. Restore. `--clean --if-exists` drops each object before recreating it; `--single-transaction` makes it all or
   nothing, so a failure leaves the database as it was.

   ```sh
   kubectl exec -i deploy/pspsps-postgres -- pg_restore -U pspsps -d pspsps \
     --clean --if-exists --no-owner --single-transaction --exit-on-error < pspsps.dump
   ```

3. Start the server again: its replicas from the chart (`1`, or `cluster.replicas` with `cluster.enabled`), or
   resume the sync. It applies any migration newer than the dump as it starts.

   ```sh
   kubectl scale deploy/pspsps --replicas=1
   ```

### Onto a new, empty database

After losing the database's volume, install the chart as usual (the server creates the schema as it starts), stop the
server as in step 1, then run step 2 and 3: the dump's tables replace the empty ones.
