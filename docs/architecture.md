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
- `PlayerSessionGrain` (key: player ID) knows the player's current tab, and the room its cat is in (Friends, below). A
  player is one cat, played from their newest tab: entering a world from a second tab takes the cat over where it
  stands, the first tab is told ("You've joined from another tab") and stops, and its moves are refused. A room only
  lets the tab that controls a cat take it out.

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

Rooms are joined by doors, in the room catalogue (`RoomCatalog`): a door (`Door`) is a round spot on a room's floor
that leads to a door of another room. The browser walks the cat onto it, then calls `GoThroughDoor`; the room's grain
lets the cat out only if it stands on that door (`RoomState.LeaveThrough`), and the next room puts it just inside
the matching door (`Door.Arrival`), off it, so it doesn't walk straight back out. Doors are the only way round in
the browser (for the keyboard, each door also has a button, shown once it has focus). `GoToRoom` goes straight to a
room's spawn point: the browser uses it to rejoin its room after a reconnect, and a map of the world will use it.

Chat goes the same way, through the room's chat filter (`ChatFilter`, a rule of `RoomState.Say`): word lists that
either star a word out or refuse the whole line, seeing through accents, look-alike letters, digits for letters,
punctuation inside a word, letters stretched three or more times and spaced-out single letters, but never spelling a
word across real words or reading a plain number as one. The lists are configuration, in
`chat-filter/chat-filter.json` next to the server (empty in the image; a deployment mounts its own with the chart's
`chatFilter` values, or sets them through environment variables), and the filter is rebuilt on every change. A file
that goes missing or can't be read, or an unknown action, is logged as a warning and the filter it had stays in force.

## Friends

A **friendship** (`Friendship`, an aggregate of its own) is a request from one player to another, pending until the
one asked accepts it. A pair of players has at most one: its key is the pair in a fixed order, so the database refuses a
second even when both ask at once (and asking someone who has asked you accepts). Only the one asked accepts or
declines; the one who asked can take it back; either friend can end it. A declined, taken back or ended friendship is
deleted, so either can ask again. A player has at most 100 friends, 20 requests of their own waiting and 50 requests
waiting for them, counted apart so that cats asking one never stop it asking or accepting others. `Status` is a
concurrency token: an answer decided on a status that changed meanwhile (a request taken back as it's accepted) is
refused, never applied. Only friends can go to each other. Guests have friends like everyone: a guest is a real
player (its friendships go with it, and stay when it saves its cat).

Where a friend is comes from the grains, not the database: the hub tells the player's `PlayerSessionGrain` which room
its current tab's cat joined, and the friends list asks each friend's session, which asks that room whether the cat is
still there, played from that tab (the room's lease is the truth). That question interleaves with the session's other
calls, so the player's own moves never wait behind friends asking, and a session asked after a player who isn't playing
goes again at once. A session that points at a room checks it every lease
(which keeps it from being collected while its player sits idle) and forgets it once the room no longer has the cat. A
friend between rooms, in the world picker or offline has no place. If the pod holding a session grain dies, the grain
comes back empty and its player shows offline until they next enter a room. "Go to" a friend takes a place in their
world first (within its cap), then joins their room (within its cap), and only then leaves the room and world the
player was in, so a full world or room leaves the player where they were. A change to a friendship is sent to the other
player's tabs (`FriendsChanged`, through SignalR's user = player ID).

## Shop

The shop (`ShopCatalog`) sells hats, collars and fur colours for coins, which come from fishing. Buying and wearing are
rules of the `Player` aggregate: a player buys what they can afford and don't have yet (`Buy`), and has it from then on
(`player_items`); every cat also owns the coat it was born with. A cat wears at most one hat and one collar, and always
one fur (`Wear`, `TakeOff`). Guests shop like everyone.

Coins never go below zero, however many purchases and catches land at once. A catch adds its coins in one statement
(`coins = coins + n`); a purchase reads the player, applies the rule and saves, with `Coins` as a concurrency token: a
purchase whose coins changed meanwhile (a catch, a purchase in another tab) is refused by the database and decided
again on the coins there are now (`ShopService`). A check constraint keeps `coins >= 0` whatever the code does, and
the key of `player_items` (player, item) keeps a thing from being bought twice.

What a cat wears is saved on the player, comes with it into every room (`CatView`, so later arrivals see it in the
snapshot), and a change is shown live: the hub saves it, then tells the room the player's cat is in
(`IRoomGrain.Dress`), which updates its `RoomState` and broadcasts `CatDressed`. The browser draws the hats and
collars in code (`client/src/wearables.ts`), on the cat model's head-top and neck anchors.

## Layers (Clean Architecture)

Dependencies point inwards only: **Domain ← Application ← Infrastructure ← Server**. `Pspsps.Architecture.Test`
enforces this.

- **Domain** (`src/Pspsps.Domain`): the game's rules, no frameworks. Aggregates in the DDD sense: `Player` owns its
  logins, coins, fur and what it has from the shop. A private constructor for EF Core, named static factories (`CreateGuest`,
  `CreateWithLogin`), private setters, collections exposed read-only, and methods that refuse invalid changes with a
  `DomainException`. `Friendship` is another aggregate (Friends, above). Also the in-memory world and room rules
  (`WorldPopulation`, `RoomState`, `SessionLease`) and the world and room catalogues.
- **Application** (`src/Pspsps.Application`): use cases (`PlayerService`, `FriendService`, `ShopService`) and the ports they need
  (`IApplicationDbContext`).
- **Infrastructure** (`src/Pspsps.Infrastructure`): EF Core on PostgreSQL (snake_case names), the migrations, and the
  Data Protection key ring in the database.
- **Server** (`src/Pspsps.Server`): the host. Sign-in endpoints, the SignalR hub, and the Orleans grains. The hub and
  the grains stay thin: rules belong in the Domain.

## Sign-in

Discord and Google sign in through OAuth into a short external cookie; `/auth/callback` finds or creates the player
for that login and swaps it for the game's own cookie, which holds only the player ID. Guests get a player with a
generated name and the same cookie. A provider is offered only when its client ID is configured.

A guest keeps their cat with "Save your cat": a POST to `/auth/link/{provider}` from the game's own page (other sites
are refused by `Sec-Fetch-Site` or `Origin`) starts the same OAuth sign-in, marked (in the
protected `AuthenticationProperties`) with the player who asked, and `/auth/callback` then adds the login to that
player (`Player.LinkLogin`) instead of finding or creating one, if the game cookie is still that player's. The cat,
its ID and coins stay; it stops being a guest. A login that already has another cat is refused (`?link=taken`), never
merged, and the guest stays signed in as themselves (also when two requests race for the login). A second login from
a provider the cat already has is `?link=refused` (a unique index on `player_logins (player_id, provider)` holds that
rule when two links race). A provider sign-in that fails on the way back (Cancel on the consent screen, lost state)
returns to `?link=failed` or `?signin=failed` instead of an error page, and logs a warning.

The cookie is encrypted with ASP.NET Core Data Protection, whose keys are kept in PostgreSQL so every pod can read
every cookie.

## Database changes

Change the model in the Domain and its mapping in `Infrastructure/Persistence/Configurations`, then add a migration:

```sh
dotnet tool install --global dotnet-ef
cd src/Pspsps.Infrastructure && dotnet ef migrations add <Name> --output-dir Migrations
```

Never edit a migration that is already on master; add a new one. The server applies migrations on startup.
