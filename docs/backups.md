# Backups

Players' data lives only in the chart's PostgreSQL. With `backup.enabled`, a CronJob dumps it on `backup.schedule`
with `pg_dump --format=custom` onto a volume (the chart's own PersistentVolumeClaim, or
`backup.persistence.existingClaim`) and keeps the newest `backup.keep` dumps. The script is
`charts/pspsps/files/backup.sh`; CI runs it and both restores below against a real PostgreSQL
(`tests/chart/backup-test.sh`).

Dumps are named `pspsps-<UTC time>.dump`, e.g. `pspsps-20261117T030000Z.dump`. Each run:

1. waits for the database to answer (a new pod's network can take a moment to be allowed): it tries every 5 s until
   `backup.connectTimeoutSeconds` have passed, then checks it can log in, and stops with nothing changed - saying why
   (a name that doesn't resolve, refused, timed out) - when it can't;
2. writes the new dump to a `.partial` file of its own and renames it when `pg_dump` succeeds, so a failed run never
   counts as a backup or costs one. A `.partial` file older than `backup.activeDeadlineSeconds` is from a run that
   died, and is deleted; younger ones may belong to a run still going, and are left alone. A run stopped by a signal
   (its deadline, a deleted pod) stops `pg_dump` and deletes its `.partial` file;
3. only then deletes the oldest dumps beyond `backup.keep`.

Size the volume for `backup.keep + 1` dumps. If it fills up anyway, the run deletes the oldest dump (never the only
one) and tries once more, so a growing database doesn't stop backups for good.

The job uses the database's own image, so `pg_dump` is always the server's version. The chart's backup claim has
`helm.sh/resource-policy: keep` (`backup.persistence.keepOnUninstall`), so uninstalling the chart leaves the dumps.

The commands below assume the release is named `pspsps` in namespace `pspsps`; with another release name, the objects
are `<release>-pspsps-postgres` and so on (or `<release>-postgres` when the release name contains `pspsps`).

```sh
kubectl config set-context --current --namespace pspsps
```

## Taking one now

```sh
kubectl create job --from=cronjob/pspsps-backup pspsps-backup-manual
# Until it completes or fails (after its retries), at most 10 minutes; then how it ended and what it said.
timeout 10m sh -c 'until kubectl get job/pspsps-backup-manual \
  -o jsonpath="{.status.conditions[?(@.status==\"True\")].type}" | grep -qE "Complete|Failed"; do sleep 5; done'
kubectl get job/pspsps-backup-manual -o jsonpath='{.status.conditions[?(@.status=="True")].type}{"\n"}'
kubectl logs job/pspsps-backup-manual
kubectl delete job pspsps-backup-manual
```

## Getting a dump out

The dumps are on the backup volume, which only the backup job mounts. Start a pod that mounts it (same image, user and
security settings as the job, so it pulls from wherever `postgres.image` does and a restricted namespace admits it),
copy a dump out, and delete the pod. With `backup.persistence.existingClaim`, put that claim's name in `claimName`.

```sh
# The chart's postgres.image, as the database runs it.
image=$(kubectl get deploy/pspsps-postgres -o jsonpath='{.spec.template.spec.containers[0].image}')
kubectl apply -f - <<YAML
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
    fsGroupChangePolicy: OnRootMismatch
    seccompProfile: { type: RuntimeDefault }
  containers:
    - name: shell
      image: $image
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

This replaces the database with the dump: everything written since the dump is lost. The database is dropped and
created again empty before the restore, so nothing newer than the dump survives (tables or columns a later migration
added, rows in `__EFMigrationsHistory`); the server then migrates forward from the dump's migrations as it starts. The
same steps restore onto a new, empty database, e.g. after losing the database's volume: install the chart as usual
first.

1. Stop the backups and the server, so nothing connects while the restore runs: a dump holding a connection makes
   `dropdb` fail, and one taken mid-restore would be of an empty database. Argo CD or another controller syncing the
   chart will undo both: pause its sync first.

   ```sh
   kubectl patch cronjob/pspsps-backup -p '{"spec":{"suspend":true}}'
   # Until no backup (scheduled or manual) is running.
   until [ -z "$(kubectl get pods -l app.kubernetes.io/instance=pspsps,app.kubernetes.io/component=backup \
     --field-selector=status.phase!=Succeeded,status.phase!=Failed -o name)" ]; do sleep 5; done
   kubectl scale deploy/pspsps --replicas=0
   kubectl wait --for=delete pod -l app.kubernetes.io/instance=pspsps,app.kubernetes.io/component=server --timeout=2m
   ```

2. Drop the database and create it again, owned by the app's role (`postgres.username`; the database's name is
   `postgres.database`), then restore into it. `--single-transaction` makes the restore all or nothing: if it fails,
   the database is empty and you can run it again.

   ```sh
   kubectl exec deploy/pspsps-postgres -- dropdb -U pspsps pspsps
   kubectl exec deploy/pspsps-postgres -- createdb -U pspsps -O pspsps pspsps
   kubectl exec -i deploy/pspsps-postgres -- pg_restore -U pspsps -d pspsps \
     --no-owner --single-transaction --exit-on-error < pspsps.dump
   ```

3. Start the server again, with its replicas from the chart (`1`, or `cluster.replicas` with `cluster.enabled`), and
   resume the backups (or resume the sync). The server applies any migration newer than the dump as it starts.

   ```sh
   kubectl scale deploy/pspsps --replicas=1
   kubectl patch cronjob/pspsps-backup -p '{"spec":{"suspend":false}}'
   ```
