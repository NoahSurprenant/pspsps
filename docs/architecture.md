# Architecture

## The pieces

```
browser (Babylon.js client)
   │  one SignalR connection (/hubs/game) + a few HTTP calls (/auth, /api)
   ▼
server pods ── every pod is the same: ASP.NET Core + SignalR hub + an Orleans silo
   │   ├─ Redis: Orleans' membership table (which pods are alive), the SignalR backplane, the moderation signal
   │   └─ PostgreSQL: players and everything else that lasts (EF Core), and the shared key ring
```

To handle more players, add replicas: the chart's cluster mode ([cluster.md](cluster.md)). With one replica and no Redis
configured, the server runs a single local silo, which is what development and the tests use (`Cluster:Mode`,
`OrleansSetup`). The browser's hub connection is a WebSocket opened straight away, with no negotiate request, so it stays
on the pod it opened on and the pods need no sticky sessions; a browser that can't open one falls back to SignalR's
negotiation (`client/src/net.ts`, `hubTransports`).

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
over by a newer tab, or gone); those tabs leave its SignalR group, are told (`RoomLost`), and go back to the room. A
room on a pod that is stopping (a rolling update) doesn't wait for that: it tells its tabs `RoomLost` as it
deactivates.

A move goes like this:

1. The browser calls `Move(x, z)` on the hub, on whichever pod its connection is on.
2. The hub calls the room's grain, wherever it lives.
3. The grain checks the move against the room's rules (`RoomState`), then broadcasts it to the room's SignalR group.
4. The Redis backplane carries the broadcast to every pod with a connection in that group.

Rooms are joined by doors, in the room catalogue (`RoomCatalog`): a door (`Door`) is a round spot on a room's floor
that leads to a door of another room. The browser walks the cat onto it, then calls `GoThroughDoor`; the room's grain
lets the cat out only if it stands on that door (`RoomState.LeaveThrough`), and the next room puts it just inside
the matching door (`Door.Arrival`), off it, so it doesn't walk straight back out. Doors are the only way round in
the browser (for the keyboard, each door also has a button, shown once it has focus: so do the player's own cat and,
in the shop, the catalogue). `GoToRoom` goes straight to a
room's spawn point: the browser uses it to rejoin its room after a reconnect, and a map of the world will use it.

A room's props (`Prop`: benches, trees, café tables) are in the catalogue too, and go to the browser with the room;
they are decoration, which cats walk through. `PropPlacement` keeps them out of the way: on the floor, off the doors,
the pond and where cats fish from, the spawn point, and the straight walks between all of these. The browser draws
each kind in the room's flat, outlined style, all of a room's props in one mesh, and paints the floors and back walls
in code on the way into a room (`client/src/looks.ts`): no image files. One prop is more than decoration: the shop's
catalogue stand (Shop, below).

The room bar is small, to leave the room in view: the coins, Friends and Worlds. The room's name is the page's title
("Cat Café · pspsps"), which the tab and screen readers say. A tap on the room goes to the first of these along its
ray, not the nearest: another cat (its card), a door, the pond, the player's own cat (their own card), the catalogue
stand, a prop (the cat walks beside it), the floor. So a door just behind the player's own cat still takes the tap.

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

The shop is a place: the Cat Shop, a room of the catalogue (`RoomCatalog.ShopId`) whose door is one of the shopfronts in
Town Square's back wall. It sells (`ShopCatalog`) hats, collars, fur colours and furniture for coins, which come from
fishing. Buying and wearing are rules of the `Player` aggregate: a player buys, from the room their cat is in, what they
can afford and don't have yet (`Buy`), and has it from then on (`player_items`); every cat also owns the coat it was born
with. Only a room with the catalogue in it sells (`RoomDefinition.HasCatalogue`: a `PropKinds.CatalogueStand` among its
props, which only the shop has); a purchase from anywhere else (another room, a home, no room) is refused with a message
saying where to go. The hub hands the use case (`ShopService.BuyAsync`) the room of the tab that asked, from its own
record of where each connection is. A cat wears at most one hat and one collar, and always one fur (`Wear`, `TakeOff`),
anywhere. Guests shop like everyone.

In the browser, a tap on the catalogue stand walks the cat up to it and opens the catalogue once it is there (at once
for a cat near it already; `client/src/catalogue.ts`); "Open the catalogue" does the same for the keyboard. What a
player has is put on and taken off from their own card, which a tap on their own cat opens (or "Your cat"). Every
player card shows a picture of the cat as it looks now, its fur, hat and collar (`client/src/portrait.ts`): the room's
cat model, dressed, drawn once into an image off screen, in a scene of its own on the room's engine that is disposed
as soon as the picture is taken; pictures are kept per outfit. Nothing more is downloaded, and nothing keeps drawing.

Coins never go below zero, however many purchases and catches land at once. A catch adds its coins in one statement
(`coins = coins + n`); a purchase reads the player, applies the rule and saves, with `Coins` as a concurrency token: a
purchase whose coins changed meanwhile (a catch, a purchase in another tab) is refused by the database and decided
again on the coins there are now (`ShopService`). A check constraint keeps `coins >= 0` whatever the code does, and
the key of `player_items` (player, item) keeps a thing from being bought twice (furniture, which can be had more than
once, is counted on its one row).

What a cat wears is saved on the player, comes with it into every room (`CatView`, so later arrivals see it in the
snapshot), and a change is shown live: the hub saves it, then tells the room the player's cat is in
(`IRoomGrain.Dress`), which updates its `RoomState` and broadcasts `CatDressed`. The browser draws the hats and
collars in code (`client/src/wearables.ts`), on the cat model's head-top and neck anchors.

## Homes

Every player has a **home** (`Home`, an aggregate of its own, keyed by its owner): a room of their own, in no world's
catalogue. Every home has the same floor plan (`Home.Layout`), with a door out by the back wall that leads to Town
Square's spawn point, in whichever world the cat came from; a cat in a home is still in that world (its place there
counts against the world's cap). A home's room grain is the same `RoomGrain`, keyed `homes/<owner>` (`RoomKey.ForHome`):
on activation it reads the home from the database instead of the catalogue. The snapshot carries the home
(`HomeView`), and the hub fills in the world the cat is in.

Furniture is a kind of shop item (`ItemKind.Furniture`), bought like the rest, and a player can have up to five of each
(`OwnedItem.Count`); every cat is born with its bed. Each piece of furniture's ID is the prop it is drawn as, so the
browser draws furniture with the rooms' own props (`client/src/props.ts`). The home's rules are on the aggregate: only
furniture the owner has goes out, each piece at most as many times as they have it, at most 40 pieces, turned a quarter
at a time, and where the rooms' props may stand (`PropPlacement`: on the floor, off the door, the spawn point and the
walks between them, and off each other). `Version` is a concurrency token, so two tabs putting pieces out at once are
decided one after the other (`HomeService`), and a home is saved only once its owner first changes it. A change is
saved, then shown to whoever is in the home (`IRoomGrain.Furnish`, `HomeChanged`).

The owner chooses who may come in (`HomeVisitors`): their friends (every home starts so), anyone, or nobody; the owner
always may (`Home.MayVisit`). Visiting checks it (`HomeService.VisitAsync`), and "Go to" a friend who is in a home visits
that home on the same terms. When the owner makes it stricter, the hub asks the home who is in it, the home's rule says
who may no longer stay (`HomeService.MustLeaveAsync`), and the room sends them out (`IRoomGrain.SendOut`): each tab
is told (`SentOut`) and goes to Town Square in its own world (or on along its way out, below). Ending a friendship asks both homes the same way.

A cat is never left in no room. When the room it was going to doesn't take it (full, a home closed to it, or a room
that couldn't be opened), the Domain's way out (`WayOut`) says where it goes instead, in order: back into the room it
left (by the door it went out of, if it took one), unless that is another player's home, which may no longer let it in;
then the world's rooms, Town Square first, then the café and the shop; then its own home, which always has room for its
owner (`RoomState` lets a home's owner into it however full it is). So a cat sent out of a home into a full Town Square
lands in the café, and a world whose Town Square is full is entered by its next room. The hub tries each in turn, and a
cat that lands somewhere it wasn't going gets that room with why (`RoomSnapshot.Notice`, "Town Square is full. You went
to Cat Café instead."), which the browser says. Only when nothing at all answers (the grains can't be reached) does the
connection keep the room it left as its place, whose next renewal finds the cat gone and sends `RoomLost`, and the
browser asks again.

In the browser, the way home is at the top of the friends list (the room bar has no room for another button on a folded
phone). In their own home, the owner has Decorate and who can visit in the dock; decorating (`client/src/home.ts`) picks
a piece to put out with a tap on the floor (or "Find a spot", for the keyboard), and selects a piece with a tap (or its
button) to turn it or put it away.

## Mutes and reports

A player protects themself from another from that cat's card (a tap on it): **Mute** and **Report**, under the friend
buttons. Nothing more on screen: the room bar and the dock stay as they are.

A **mute** (`Mute`, an aggregate of its own, keyed by the pair: muter, muted) is the player's own choice, with no
review: their browser stops showing the muted player's chat lines and bubbles (the room's history included, when they
come into a room), while the muted cat itself still shows, and nobody else is affected. Not yourself, not twice, and at
most 500 (`Mute.MaxMutes`). Mutes are saved, so they last across reloads, tabs and devices: the browser asks for them
(`GetMutes`) when it connects, before any room, and every change is sent to all the player's tabs (`MutesChanged`).
A muted cat's card says so and has Unmute; the friends list has a Muted section, to see and undo them all.

A **report** (`Report`, an aggregate of its own) gives a reason from a short list (`ReportReason`: mean or hurtful chat,
an inappropriate name, spam, something else) and an optional note (at most 300 characters), and by default mutes the
cat too ("Also mute them", in the same save). The server keeps who reported whom (and the reported cat's name then),
the world and room the reporter was in, when, and the room's last chat lines (`ReportedLine`, at most 20, each with its
speaker's ID and name and when it was said), read from the room's grain (`IRoomGrain.RecentChat`): never what the
browser says was said. The lines are kept only when the reported cat is in that room or said one of them: a cat
reported from a card opened elsewhere (the friends list) is reported without strangers' chat. The Domain refuses reporting
yourself, more than 10 reports in an hour, and a second open report of the same cat within the hour; a player with every
mute used still reports, unmuted. Every report starts `Open`, for the admins to decide on `/admin` (Moderation, below);
nothing in the game shows reports. Reports have no foreign keys to the players, so deleting either player leaves the
report for review.
Guests mute and report like everyone.

## Moderation

Admins are listed by player ID in the server's settings (`Moderation:AdminPlayerIds`; the chart's
`moderation.adminPlayerIds`), the only way to become one; a guest can be an admin. A player finds their own ID on their
card (Details), and `/admin` shows it to anyone signed in who isn't an admin (403). The list is read again whenever the
configuration changes.

What an admin did to a player is a `PlayerModeration`, an aggregate of its own keyed by the player and made the first
time an admin acts on them: a ban (until a time, or for good), a mute in every room (until a time), up to when the
player has seen their warnings, and the audit trail (`ModerationEntry`: who, what, when, why, until when; a warning is
one of its lines). Only an admin acts (a `Moderator` is made only from the admin list), never on themselves nor on
another admin (the list changes admins; a ban or mute from before a player was listed can still be lifted), and always
says why: the player is shown it. A ban or mute ends at its time by itself (`ModerationClock`, a `TimeProvider` tests
move), or when an admin lifts it; a new one takes the place of the last. `Version` is a concurrency token, so two
admins at once are decided one after the other.

An admin moderates from another cat's card (Moderate, folded away: Warn, Mute for an hour, a day or a week, Ban for a
day, a week or for good, Lift), which only admins are sent the cat's standing for, and from `/admin` (`admin.html`, a
plain page of the client's with no 3D scene, served to admins only, reading `/api/admin/*`): the open reports, oldest
first, each with its chat; who is banned or muted now, with Lift; and what admins did lately. The game's own bars get
nothing new.

A report is decided on `/admin`: dismissed, or the reported player warned, muted or banned (with a reason, which they
are shown), which closes the report with that decision, who took it and when (`Report.Dismiss`, `Report.CloseWith`), in
the same save as the player's moderation (`ModerationService.DecideReportAsync`). A report is decided once (its
`Status` is a concurrency token, so two admins at once can't both close it), and never by the admin it is about. A
report about a cat that no longer exists can only be dismissed.

The hub refuses a banned player: their connection at the door (the hub's `not-banned` policy, a 403), and every call on
a connection already open (`ModerationHubFilter`), with why and until when. Whether a player is banned is read from the
database at most once every 5 seconds per player on each pod (`StandingCache`; a change made on the pod is read at
once), since every move is a call. Every moderation is signalled to every pod (`IModerationSignal`: Redis pub/sub with
several pods), and each drops what it knew of the player's standing, so it is read again at their next call; should a
pod miss the signal, its read is at most 5 seconds old. A ban is put into effect at once (`ModerationEffects`): the
player's tabs are told (`Banned`), show why and until when, and stop; every pod closes the player's connections it holds
two seconds later (`ModerationOnThisPod`), so a tab that ignores the ban is closed too; their cat is taken out of its
room, wherever it is; and `/api/me` tells a page that loads why it can't play. Chat is refused while muted
(`PlayerModeration.EnsureMayChat`, asked by `GameHub.Say` with the same per-pod read), and the player's tabs are told at
once (`Muted`), which turns their chat box into why and until when. A warning is shown (at once when the tabs are told
`Warned`, or on the player's next visit) until the player closes it, which tells the server they saw it (`SawWarning`):
two warnings meanwhile both show, one after the other. When the database can't answer, the last read holds and a player
never read plays.

### Network bans

A banned guest who clears their cookies would get a new guest cat. So the server remembers the networks each player
plays from (`PlayerAddress`: player, network, last seen), on guest sign-up, Discord or Google sign-in and every hub
connection, for 7 days after it last saw them (`PlayerAddress.KeepFor`; saved at most once an hour per network), and
forgets older ones every hour on every pod (`AddressPruning`, which runs with network bans off too). A network is never
an address: it is a keyed hash, HMAC-SHA256 with the server's secret `Moderation:AddressKey` (the chart's Secret,
`Moderation__AddressKey`, at least 32 characters) of an IPv4 address, or of an IPv6 address's /64 (a household or phone
gets a whole /64 and picks new addresses in it at will) (`ClientAddresses`). The address is the one ASP.NET Core
resolved, `HttpContext.Connection.RemoteIpAddress` after `UseForwardedHeaders`, which applies a forwarded header only
from the proxies configured as trusted (`forwardedHeaders` in the chart): a header a browser sets itself is ignored.
An address that isn't on the internet (loopback, private, link-local, unique-local, carrier-shared) is never a network:
it is the server's side of a proxy whose header isn't trusted, where every player would look the same, or a local
network; the server warns as it starts when network bans are on and no proxy is trusted. An admin's network is never
kept, and a pod saves a player's network at most once an hour.
Network bans are on exactly when the key is set, unless `Moderation:AddressBans:Enabled` (the chart's
`moderation.addressBans.enabled`) says otherwise; asked for with no key, the server refuses to start. What is kept, and
for how long: [privacy.md](privacy.md). Turned off, the next pruning forgets every network and network ban kept.

An admin banning a cat sees, under Moderate on its card, the networks it played from this week by label (the first 8 hex
digits of the hash), each with how many players were on it this week (more than one: a school, a phone network, a
household), and checks the ones to ban with it, for a day or a week. A network's ban (`AddressBan`, an aggregate of its
own, keyed by the hash) is the Domain's rules: only an admin, never their own nor another admin's network, only a
network the cat was on in the last 7 days, only while the cat is banned, with that ban's reason, for a day or a week and
never past the cat's ban (never for good: a network can be shared); a new one takes the place of the last, and it ends
at its time or when an admin lifts it. A cat banned again for less takes its networks' bans down to its new end
(`AddressBan.FollowCat`, in the same save); a cat banned already can have its networks banned alone ("Ban the checked
networks"), its own ban left as it is. Each ban and lift is a line of the cat's audit trail, with the network's label.
Lifting the cat's ban, from its card or from `/admin`, offers to lift the bans on the networks banned with it too
(checked by default); a card also shows a network banned with another cat, which is that cat's to lift. `/admin` lists
the networks banned now (label, the cat, how many players, until when, why) with Lift. Ended and lifted bans are
forgotten with the old networks.

From a banned network, a new guest is refused (403, with the same "banned until ... Why: ..." as a banned cat, and the
browser shows the banned screen), a Discord or Google sign-in makes no cat and is sent back to `/?signin=banned` (the
page asks `/auth/ban` why, and shows it), `/api/me` says the player is banned, and the hub refuses the connection at the
door (the `not-banned` policy) and every call on one opened before (`ModerationHubFilter`, with the network the
connection opened from). Tabs already open there are told at once (`Banned`, to the SignalR group of the network, which
every connection but an admin's joins as it opens, carried to every pod's tabs by the SignalR backplane), and every pod
closes the connections it holds on that network two seconds later and takes their cats out of their rooms
(`ModerationOnThisPod`, through the same cross-pod signal as a player's ban: a network-ban message carrying the network's
hash, never the address); a pod that misses the signal still refuses and closes a tab at its next call. Whether a
network is banned is read at most once every 5 seconds per network on each pod (`AddressBanCache`, as `StandingCache`
for players; a ban made on another pod is read again at once, a lift within that time). Admins (the configured list)
are never refused by a network's ban, so the owner can't lock themself out.

The end-to-end tests' server sees one address for every browser, so a network ban there would lock the whole run out:
the refusals are tested through the hosted server in the integration tests, each request from an address of its own.

## Changing what pods say to each other

During a rolling update old and new pods run side by side ([cluster.md](cluster.md)): a grain call made by a pod on the
old code can run on an activation on the new code, and a Redis message one pod publishes, the other reads. Grain
interfaces are versioned (`[Version]` on `IRoomGrain`, `IWorldGrain`, `IPlayerSessionGrain`) with Orleans' rolling
upgrade settings (`OrleansSetup.ConfigureVersioning`: `BackwardCompatible`, `AllCompatibleVersions`; see Orleans'
[grain interface versioning](https://learn.microsoft.com/dotnet/orleans/grains/grain-versioning/grain-versioning) and
[backward compatibility guidelines](https://learn.microsoft.com/dotnet/orleans/grains/grain-versioning/backward-compatibility-guidelines)).
Orleans can't check that a new version really is compatible; these rules make it so:

- **A grain interface changes only by adding.** A new method, or a new overload, never a changed signature: not a
  parameter added, removed, retyped, reordered or renamed (Orleans matches arguments by position, so swapping two names
  swaps the values), not a return type. Every change raises the interface's `[Version]` by one.
- **A method stays for a release after it's replaced.** Mark it `[Obsolete]` in the version that replaces it, keep it
  working (old pods still call it), and remove it in a later version, once no pod on the old code can be running.
- **A method's meaning doesn't change** under the same signature, except to fix a bug: a new behaviour is a new method.
- **A `[GenerateSerializer]` type changes only by adding members**, each with a new `[Id]`. An Id is never reused, even
  after its member is removed. A pod on the old code sending the type leaves a new member at its default (null, 0,
  false), not the C# default parameter value: the code reading it must treat that as "not sent". A member's type never
  changes; a new type is a new member.
- **Messages between pods outside Orleans** (the moderation signal, `ModerationMessage` and `NetworkModerationMessage`)
  carry a version and are read by their known fields only: a new field goes at the end, and an older pod ignores it.

`WireContractTests` (in `Pspsps.Server.Test`) holds the record of every grain interface version and every serialized
member's Id, `tests/Pspsps.Server.Test/Game/wire-contracts.txt`, and fails when an interface changes without a new
version, when a new version drops a method of the last that wasn't `[Obsolete]`, or when an Id changes its member. The
failure prints the record as today's code would make it: after a deliberate, compatible change, add the new lines,
keeping the old ones.

## Layers (Clean Architecture)

Dependencies point inwards only: **Domain ← Application ← Infrastructure ← Server**. `Pspsps.Architecture.Test`
enforces this.

- **Domain** (`src/Pspsps.Domain`): the game's rules, no frameworks. Aggregates in the DDD sense: `Player` owns its
  logins, coins, fur and what it has from the shop. A private constructor for EF Core, named static factories
  (`CreateGuest`, `CreateWithLogin`), private setters, collections exposed read-only, and methods that refuse invalid
  changes with a `DomainException`. `Friendship`, `Home`, `Mute`, `Report`, `PlayerModeration` and `AddressBan` are
  other aggregates (above). Also the in-memory world and room rules (`WorldPopulation`, `RoomState`, `SessionLease`)
  and the world and room catalogues.
- **Application** (`src/Pspsps.Application`): use cases (`PlayerService`, `FriendService`, `ShopService`, `MuteService`,
  `ReportService`, `HomeService`, `ModerationService`, `AddressBanService`) and the ports they need
  (`IApplicationDbContext`, `IAdmins`).
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

## Telemetry

The server exports its logs, metrics and traces over OTLP. The players' browsers can send theirs too, when the server
turns it on: their errors, the hub connection's life, a span for each HTTP and hub call, Web Vitals and the frame rate
in a room. They post batches to the server (`/api/client-telemetry`), which forwards them to the collector; nothing in
the browser talks to the collector. A hub call's trace context goes in its SignalR invocation headers, so the server's
hub span (and the grain calls under it) continues the browser's trace. The browser learns whether to send from a
`Server-Timing` mark on the page, without a request, and never sends under automation. Details in
[telemetry.md](telemetry.md).

## Database changes

Change the model in the Domain and its mapping in `Infrastructure/Persistence/Configurations`, then add a migration:

```sh
dotnet tool install --global dotnet-ef
cd src/Pspsps.Infrastructure && dotnet ef migrations add <Name> --output-dir Migrations
```

Never edit a migration that is already on master; add a new one. The server applies migrations on startup.
