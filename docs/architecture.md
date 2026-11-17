# Architecture

## The pieces

```
browser (Babylon.js client)
   │  one SignalR connection (/hubs/game) + a few HTTP calls (/auth, /api)
   ▼
server pods ── every pod is the same: ASP.NET Core + SignalR hub + an Orleans silo
   │   ├─ Redis: Orleans' membership table (which pods are alive) and the SignalR backplane
   │   └─ PostgreSQL: players and everything else that lasts (EF Core), and the shared key ring
```

To handle more players, add replicas. With one replica and no Redis configured, the server runs a single local silo,
which is what development and the tests use.

## Worlds, rooms and actors

A **world** is a separate copy of every room, with its own population cap: players pick one when they arrive. A
**room** is one place in one world ("tabby/town-square").

Both are Orleans grains, the actors of this design:

- `WorldGrain` (key: world ID) holds the world's `WorldPopulation`: who is in it, against its cap.
- `RoomGrain` (key: `world/room`) holds the room's `RoomState`: who is in it, where they stand, what they said lately.
- `PlayerSessionGrain` (key: player ID) knows the player's current tab. A player is one cat, played from their newest
  tab: entering a world from a second tab takes the cat over where it stands, the first tab is told ("You've joined
  from another tab") and stops, and its moves are refused. A room only lets the tab that controls a cat take it out.

Each grain exists exactly once in the whole cluster, on whichever pod Orleans placed it, and handles one call at a
time, so its state needs no locks. Room state lives only in memory: an empty room deactivates and forgets everything.

A tab's session (its connection) holds a lease on its world and room. The pod holding the connection keeps where each
of its connections is (`SessionHeartbeat`, the hub's one record of it) and renews their leases every 30 seconds, idle
tabs included, with one call per world and per room. The world and room grains check every 30 seconds for leases
strictly older than 100 seconds (`SessionLease`, used by `WorldPopulation` and `RoomState`), so a lease survives two
missed renewals. A tab that closes leaves at once; a pod that dies without closing its connections stops renewing
them, so within about two minutes their cats leave their rooms (the others see them go) and their tabs stop counting in
their worlds. The browsers reconnect to another pod and enter again, as new sessions.

The other way round, when a world or room grain's pod dies, the grain comes back empty on another pod. A world counts
the live tabs back in as their renewals arrive (but not tabs that left it lately: a renewal sent just before a tab left
can arrive after it); a tab it can't take back because it filled up meanwhile is told (`WorldLost`) and goes back to
the world picker. A room answers a renewal with the tabs whose cats it no longer has (`RoomState.Renew`: renewed, taken
over by a newer tab, or gone); those tabs leave its SignalR group, are told (`RoomLost`), and go back to the room.

A move goes like this:

1. The browser calls `Move(x, z)` on the hub, on whichever pod its connection is on.
2. The hub calls the room's grain, wherever it lives.
3. The grain checks the move against the room's rules (`RoomState`), then broadcasts it to the room's SignalR group.
4. The Redis backplane carries the broadcast to every pod with a connection in that group.

## Layers (Clean Architecture)

Dependencies point inwards only: **Domain ← Application ← Infrastructure ← Server**. `Pspsps.Architecture.Test`
enforces this.

- **Domain** (`src/Pspsps.Domain`): the game's rules, no frameworks. Aggregates in the DDD sense: `Player` owns its
  logins, coins and fur. A private constructor for EF Core, named static factories (`CreateGuest`,
  `CreateWithLogin`), private setters, collections exposed read-only, and methods that refuse invalid changes with a
  `DomainException`. Also the in-memory world and room rules (`WorldPopulation`, `RoomState`,
  `SessionLease`) and the world and room catalogues.
- **Application** (`src/Pspsps.Application`): use cases (`PlayerService`) and the ports they need
  (`IApplicationDbContext`).
- **Infrastructure** (`src/Pspsps.Infrastructure`): EF Core on PostgreSQL (snake_case names), the migrations, and the
  Data Protection key ring in the database.
- **Server** (`src/Pspsps.Server`): the host. Sign-in endpoints, the SignalR hub, and the Orleans grains. The hub and
  the grains stay thin: rules belong in the Domain.

## Sign-in

Discord and Google sign in through OAuth into a short external cookie; `/auth/callback` finds or creates the player
for that login and swaps it for the game's own cookie, which holds only the player ID. Guests get a player with a
generated name and the same cookie. A provider is offered only when its client ID is configured.

The cookie is encrypted with ASP.NET Core Data Protection, whose keys are kept in PostgreSQL so every pod can read
every cookie.

## Database changes

Change the model in the Domain and its mapping in `Infrastructure/Persistence/Configurations`, then add a migration:

```sh
dotnet tool install --global dotnet-ef
cd src/Pspsps.Infrastructure && dotnet ef migrations add <Name> --output-dir Migrations
```

Never edit a migration that is already on master; add a new one. The server applies migrations on startup.
