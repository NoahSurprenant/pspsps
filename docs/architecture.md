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

- `WorldGrain` (key: world ID) counts who is in the world, against its cap.
- `RoomGrain` (key: `world/room`) holds the room's `RoomState`: who is in it, where they stand, what they said lately.
- `PlayerSessionGrain` (key: player ID) knows the player's current tab. A player is one cat, played from their newest
  tab: entering a world from a second tab takes the cat over where it stands, the first tab is told ("You've joined
  from another tab") and stops, and its moves are refused. A room only lets the tab that controls a cat take it out.

Each grain exists exactly once in the whole cluster, on whichever pod Orleans placed it, and handles one call at a
time, so its state needs no locks. Room state lives only in memory: an empty room deactivates and forgets everything.

A move goes like this:

1. The browser calls `Move(x, z)` on the hub, on whichever pod its connection is on.
2. The hub calls the room's grain, wherever it lives.
3. The grain checks the move against the room's rules (`RoomState`), then broadcasts it to the room's SignalR group.
4. The Redis backplane carries the broadcast to every pod with a connection in that group.

Chat goes the same way, through the room's chat filter (`ChatFilter`, a rule of `RoomState.Say`): word lists that
either star a word out or refuse the whole line, seeing through accents, look-alike letters, digits for letters,
stretched letters and spaced-out single letters, but never spelling a word across real words. The lists are
configuration, in `chat-filter/chat-filter.json` next to the server (a few placeholders in the repository; a deployment
mounts its own, with the chart's `chatFilter` values), reread when the file changes. A file that goes missing or
can't be read is logged as a warning, and the last good lists stay in force.

## Layers (Clean Architecture)

Dependencies point inwards only: **Domain ← Application ← Infrastructure ← Server**. `Pspsps.Architecture.Test`
enforces this.

- **Domain** (`src/Pspsps.Domain`): the game's rules, no frameworks. Aggregates in the DDD sense: `Player` owns its
  logins, coins and fur. A private constructor for EF Core, named static factories (`CreateGuest`,
  `CreateWithLogin`), private setters, collections exposed read-only, and methods that refuse invalid changes with a
  `DomainException`. Also the in-memory room rules (`RoomState`) and the world and room catalogues.
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

## Known gaps

- If a pod dies without its connections closing cleanly, their cats stay in rooms on other pods until the room
  empties. A per-connection heartbeat to the room grains will fix this.
