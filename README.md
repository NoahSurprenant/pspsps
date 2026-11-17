# pspsps

A social browser game where everyone is a cat. Pick a world, wander between rooms, chat with other cats, earn coins
and decorate your place.

## Stack

- **Client:** [Babylon.js](https://www.babylonjs.com/) in TypeScript, built with Vite (`client/`).
- **Server:** ASP.NET Core (.NET 10) with SignalR for the realtime connection and
  [Orleans](https://learn.microsoft.com/dotnet/orleans/) for worlds and rooms, so the server scales out across pods
  (`src/`).
- **Database:** PostgreSQL through Entity Framework Core.
- **Sign-in:** Discord, Google, or play as a guest.
- **Deployment:** a container image and the Helm chart in `charts/pspsps`.

The backend follows Clean Architecture and domain-driven design: see [docs/architecture.md](docs/architecture.md).

## Running locally

You need the .NET 10 SDK, Node 24 and a PostgreSQL server.

```sh
# Server (http://localhost:5080). Orleans runs a single local silo; no Redis needed.
ConnectionStrings__Pspsps="Host=localhost;Database=pspsps;Username=postgres;Password=postgres" \
  dotnet run --project src/Pspsps.Server

# Client (http://localhost:5173), proxying /auth, /api and /hubs to the server
cd client && npm ci && npm run dev
```

The database schema is migrated on startup, once PostgreSQL answers: the server waits up to
`Database__StartupWaitSeconds` (90 by default, from 1 to 120), trying every 2 s and logging each failed attempt, then
migrates anyway so the real error shows. Discord and Google sign-in turn on when their client ID and secret are
configured (`Authentication__Discord__ClientId`, `Authentication__Discord__ClientSecret`, and the same for `Google`);
guest play always works.

## Tests

```sh
dotnet test Pspsps.slnx        # the integration tests start PostgreSQL with Testcontainers (Docker)
cd client && npm run lint && npm test
```

End-to-end tests (`e2e/`, Playwright) drive the running game in Chromium: a guest picks a world and the room draws,
and two players see each other walk, chat and leave. CI runs them against the Docker image; locally, start the server
and run `cd e2e && npm ci && npx playwright install chromium && E2E_BASE_URL=http://localhost:5080 npm test`.

## Deploying

Every push to master publishes a signed image (`ghcr.io/noahsurprenant/pspsps:master`) and, when the chart changed, a
signed Helm chart (`oci://ghcr.io/noahsurprenant/charts/pspsps`). The chart runs the server and PostgreSQL by
default; `cluster.enabled` makes it several server pods joined through Redis. Its `values.yaml` documents every
setting, including the Secret it reads. `backup.enabled` adds a scheduled `pg_dump` of the database; restoring one is
in [docs/backups.md](docs/backups.md).
