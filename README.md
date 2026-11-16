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

The database schema is migrated on startup. Discord and Google sign-in turn on when their client ID and secret are
configured (`Authentication__Discord__ClientId`, `Authentication__Discord__ClientSecret`, and the same for `Google`);
guest play always works.

## Tests

```sh
dotnet test Pspsps.slnx        # the integration tests start PostgreSQL with Testcontainers (Docker)
cd client && npm run lint && npm test
```
