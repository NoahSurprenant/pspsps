using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Orleans.Runtime;
using Pspsps.Application.Friends;
using Pspsps.Application.Homes;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Application.Rooms;
using Pspsps.Application.Safety;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;
using Pspsps.Domain.Shop;
using Pspsps.Domain.Worlds;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Moderation;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Hubs;

/// <summary>
/// The browser's one realtime connection. Thin: it finds the player and forwards to the world and room grains, which
/// may live on any pod. Each connection is in at most one world and one room; where it is, is kept by this pod's
/// <see cref="SessionHeartbeat"/>, which renews its leases there.
/// </summary>
[Authorize]
public sealed partial class GameHub(
    IGrainFactory grains,
    IPlayerService players,
    IFriendService friends,
    IShopService shop,
    IRoomLibrary rooms,
    IHomeService homes,
    IModerationService moderation,
    IModerationEffects moderationEffects,
    StandingCache standings,
    IAddressBanService networks,
    ClientAddresses addresses,
    IMuteService mutes,
    IReportService reports,
    SessionHeartbeat heartbeat,
    TimeProvider time,
    GameMetrics metrics,
    ILogger<GameHub> logger)
    : Hub<IGameClient>
{
    /// <summary>What a replaced tab is told; the client shows the same words.</summary>
    public const string ReplacedMessage = "You've joined from another tab.";

    // How long GoSomewhere's whole way-out loop may take: each place it tries can itself take about 30 s (the grains'
    // response timeout), so without a limit an unreachable silo could keep a hub call hanging for minutes. Past this, it
    // stops and falls back exactly as when every place in WayOut.For refuses: the connection keeps the room it left, and
    // the next renewal finds the cat gone and sends RoomLost.
    private static readonly TimeSpan WayOutTimeLimit = TimeSpan.FromSeconds(10);

    /// <summary>What a call that finishes after its connection closed answers (nobody reads it).</summary>
    public const string ClosedMessage = "This connection has closed.";

    /// <summary>What a catch that couldn't be saved answers: nothing was paid or shown.</summary>
    public const string CatchNotPaidMessage = "Your catch couldn't be counted, so it got away. Cast again.";

    public async Task<IReadOnlyList<WorldSummary>> GetWorlds()
    {
        var summaries = new List<WorldSummary>();
        foreach (var world in WorldCatalog.All)
        {
            var population = await grains.GetGrain<IWorldGrain>(world.Id).GetPopulation();
            summaries.Add(new WorldSummary(world.Id, world.Name, population, world.Capacity));
        }

        return summaries;
    }

    public async Task<RoomSnapshot> EnterWorld(string worldId)
    {
        var world = WorldCatalog.Find(worldId) ?? throw new HubException("That world doesn't exist.");
        var was = Here;
        MoveTo(null);
        await Leave(was);

        // Entering a world makes this tab the player's one; the tab it replaces is told, and stops.
        var replaced = await Session.Claim(Context.ConnectionId);
        if (heartbeat.IsClosed(Context.ConnectionId))
        {
            // The tab closed while claiming (a refresh, say): a dead connection mustn't be the player's tab, nor replace the
            // live one. The disconnect released it before the claim landed, so release it again.
            await Session.Release(Context.ConnectionId);
            throw new HubException(ClosedMessage);
        }

        if (replaced is not null)
        {
            await Clients.Client(replaced).SessionReplaced();
        }

        var worldGrain = grains.GetGrain<IWorldGrain>(world.Id);
        if (!await worldGrain.TryEnter(PlayerId, Context.ConnectionId))
        {
            throw new HubException($"{world.Name} is full. Try another world.");
        }

        if (!MoveTo(new ConnectionPlace(PlayerId, world.Id, null)))
        {
            await worldGrain.Leave(PlayerId, Context.ConnectionId);
            throw new HubException(ClosedMessage);
        }

        metrics.EnteredWorld(world.Id);
        var townSquare = RoomKey.For(world.Id, rooms.Start.Id);
        try
        {
            return await JoinRoom(world.Id, townSquare, throughDoor: null);
        }
        catch (Exception e) when (e is not OperationCanceledException && !heartbeat.IsClosed(Context.ConnectionId))
        {
            // Town Square is full (or couldn't answer): the next place in the world with room, not no room.
            if (await GoSomewhere(new ConnectionPlace(PlayerId, world.Id, null), townSquare, e) is { } landed)
            {
                return landed;
            }

            throw;
        }
    }

    public async Task<RoomSnapshot> GoToRoom(string roomId)
    {
        if (Here is null)
        {
            throw new HubException("Pick a world first.");
        }

        await EnsureCurrentTab();
        var room = rooms.Find(roomId) ?? throw new HubException("That room doesn't exist.");
        // Read after the wait: the heartbeat may have moved it meanwhile (a lost room or world).
        var place = Here ?? throw new HubException("Pick a world first.");
        return await MoveInto(place, RoomKey.For(place.WorldId, room.Id));
    }

    /// <summary>Goes into the player's own home, from wherever in the world they are.</summary>
    public Task<RoomSnapshot> GoHome() => VisitHome(PlayerId);

    /// <summary>
    /// Goes into a player's home, if its owner lets this player in (<see cref="Home.MayVisit"/>), at its spawn point by the
    /// door. The player stays in the world they came from: the home's door leads back to its Town Square.
    /// </summary>
    public async Task<RoomSnapshot> VisitHome(Guid ownerId)
    {
        if (Here is null)
        {
            throw new HubException("Pick a world first.");
        }

        await EnsureCurrentTab();
        await PlayersRule(() => homes.VisitAsync(PlayerId, ownerId, Context.ConnectionAborted));
        // Read after the waits: the heartbeat may have moved it meanwhile (a lost room or world).
        var place = Here ?? throw new HubException("Pick a world first.");
        var key = RoomKey.ForHome(ownerId);

        // Not refused as "You're home already": a room deactivating with cats still in it tells each tab RoomLost
        // itself (RoomGrain.TellLost), which doesn't go through SessionHeartbeat.LeaveLostRoom, so the hub's place for
        // this tab can still point at that same, now-lost home. The client answers RoomLost by calling VisitHome again;
        // MoveInto treats place.RoomKey == key as already left (skips Leave) and walks it back in.
        var snapshot = await MoveInto(place, key);
        // Home, or not let in and landed somewhere else on the way out (the snapshot says why): nothing to check again.
        if (ownerId == PlayerId || snapshot.Home?.OwnerId != ownerId)
        {
            return snapshot;
        }

        // Checked again, now the cat is in: an owner who made the home stricter meanwhile looked at who was in it when
        // this cat was still on its way, and missed it. Either that look sees the cat, or this check sees the change.
        try
        {
            await homes.VisitAsync(PlayerId, ownerId, CancellationToken.None);
        }
        catch (DomainException e)
        {
            var refused = new HubException(e.Message);
            if (await BackOut(place, key, refused) is { } landed)
            {
                return landed;
            }

            throw refused;
        }
        catch (Exception)
        {
            // Couldn't be checked: not let in, rather than in a home the browser doesn't know it is in. The home did
            // open (the check is what failed), so BackOut is told that, rather than the generic "couldn't be opened".
            var refused = new HubException("Couldn't check that you may visit.");
            if (await BackOut(place, key, refused) is { } landed)
            {
                return landed;
            }

            throw;
        }

        return snapshot;
    }

    // Takes the cat out of the home <key> it may not stay in (<reason>): back into the room it came from (null: the browser
    // still shows it), or somewhere else on its way out (that place's snapshot, saying why).
    private async Task<RoomSnapshot?> BackOut(ConnectionPlace from, string key, Exception reason)
    {
        var now = Here;
        if (now?.RoomKey != key || !MoveTo(now with { RoomKey = null }))
        {
            return null;
        }

        await LeaveRoom(key);
        return await GoSomewhere(from, key, reason);
    }

    // Somewhere to be, for a cat coming from <from> that the room <refused> didn't take (<reason>): the first place on its
    // way out that takes it (the Domain's WayOut: back into the room it left, unless that's another player's home, which
    // may no longer let it in; then the world's rooms; then its own home, which always has room for its owner). Null when
    // that is the room it left (by the door <throughDoor> it went out of): the browser still shows it, and says why the
    // cat stayed. Otherwise the snapshot of where it landed, saying why it is there. Never in no room: when nothing
    // answers, the connection keeps the room it left as its place, whose next renewal finds the cat gone and sends
    // RoomLost, and the browser comes back in by the way it always does.
    private async Task<RoomSnapshot?> GoSomewhere(
        ConnectionPlace from, string refused, Exception reason, string? throughDoor = null)
    {
        if (heartbeat.IsClosed(Context.ConnectionId))
        {
            return null;
        }

        var cameFrom = from.RoomKey is { } was ? RoomKey.WhereaboutsOf(was) : null;
        var deadline = time.GetUtcNow() + WayOutTimeLimit;
        foreach (var place in WayOut.For(rooms.All, PlayerId, cameFrom, RoomKey.WhereaboutsOf(refused)))
        {
            var timeLeft = deadline - time.GetUtcNow();
            if (timeLeft <= TimeSpan.Zero)
            {
                break;
            }

            var key = RoomKey.For(from.WorldId, place);
            var back = place == cameFrom;
            var joining = JoinRoom(from.WorldId, key, back ? throughDoor : null);
            if (await Task.WhenAny(joining, Task.Delay(timeLeft, time)) != joining)
            {
                // Nothing answered within what's left of the time limit: stop rather than wait out every remaining
                // place's own ~30 s grain timeout too.
                LogWayOutTimedOut(logger, PlayerId, key);
                break;
            }

            try
            {
                var snapshot = await joining;
                return back ? null : snapshot with { Notice = Landed(reason, snapshot) };
            }
            catch (HubException e) when (!heartbeat.IsClosed(Context.ConnectionId))
            {
                // An ordinary refusal (the room is full, or its owner shut it, or JoinRoom didn't find a profile): the
                // rule, not a fault, so it's expected and logged quietly rather than flooding Warning.
                LogRoomRefused(logger, e, PlayerId, key);
            }
            catch (Exception e) when (e is not OperationCanceledException && !heartbeat.IsClosed(Context.ConnectionId))
            {
                // A real failure (a timeout, say) may still have gone through on the grain's side even though this call
                // didn't hear back: best effort to take the cat back out, so it isn't left there while the loop moves on.
                await BestEffortLeave(key);
                LogNotLandedIn(logger, e, PlayerId, key);
            }
        }

        MoveTo(from);
        return null;
    }

    // Best effort: leaves the room <key> after a failed join that may have gone through anyway (a timeout doesn't say
    // which). A failure here is logged and otherwise ignored: the lease will run out on its own.
    private async Task BestEffortLeave(string key)
    {
        try
        {
            await grains.GetGrain<IRoomGrain>(key).Leave(PlayerId, Context.ConnectionId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogLeaveAfterFailedJoinFailed(logger, e, PlayerId, key);
        }
    }

    // What a cat that landed in <landed> is told: why it isn't where it was going (a rule's own words; any other failure,
    // plainly), and where it went instead.
    private string Landed(Exception reason, RoomSnapshot landed)
    {
        var why = reason is HubException or GameRuleException ? reason.Message : "That room couldn't be opened just now.";
        var where = landed.Home?.OwnerId == PlayerId ? "home" : $"to {landed.RoomName}";
        return $"{why} You went {where} instead.";
    }

    /// <summary>Puts out a piece of the player's furniture in their home, at (x, z) or, with no spot, wherever it fits.</summary>
    public Task<HomeView> PlaceFurniture(string itemId, double? x, double? z, int quarterTurns) =>
        Furnish(() => homes.PlaceAsync(PlayerId, itemId, x, z, quarterTurns, Context.ConnectionAborted));

    /// <summary>Turns a piece of furniture in the player's home a quarter.</summary>
    public Task<HomeView> TurnFurniture(Guid pieceId) =>
        Furnish(() => homes.TurnAsync(PlayerId, pieceId, Context.ConnectionAborted));

    /// <summary>Puts a piece of furniture in the player's home away.</summary>
    public Task<HomeView> PutAwayFurniture(Guid pieceId) =>
        Furnish(() => homes.PutAwayAsync(PlayerId, pieceId, Context.ConnectionAborted));

    /// <summary>
    /// Changes who may visit the player's home ("Friends", "Anyone" or "Nobody"). Cats in it who no longer may (the home's
    /// rule) are sent out, and told why: their tab takes them to Town Square in their own world (or, when it is full, the
    /// next place on their way out: WayOut).
    /// </summary>
    public async Task<HomeView> LetIntoHome(string visitors)
    {
        if (!Enum.TryParse<HomeVisitors>(visitors, out var choice) || !Enum.IsDefined(choice))
        {
            throw new HubException("That isn't a choice.");
        }

        var home = await Furnish(() => homes.LetInAsync(PlayerId, choice, Context.ConnectionAborted));
        // Saved by now: a send-out that fails leaves its cats until they leave, and the owner keeps their setting.
        await SendOutWhoMayNotStay(
            PlayerId, null, () => Task.FromResult($"{home.OwnerName} changed who can visit their home, so you had to leave."));

        return home;
    }

    // Out of the room the connection is in (if any) and into the room <key>, at its spawn point.
    private async Task<RoomSnapshot> MoveInto(ConnectionPlace place, string key)
    {
        if (!MoveTo(place with { RoomKey = null }))
        {
            // The tab closed meanwhile; the disconnect leaves the room it was in.
            throw new HubException(ClosedMessage);
        }

        // was == key: walking back into the very room this tab's place still names (RoomGrain.TellLost told it RoomLost
        // without clearing this place; see VisitHome). Leave would only wake the fresh, empty activation that answers
        // for <key> now, whose GoIfEmpty immediately deactivates it again (nothing to leave: this tab was never in it) -
        // so treat the old place as already left instead of calling Leave at all.
        if (place.RoomKey is { } was && was != key)
        {
            try
            {
                await LeaveRoom(was);
            }
            catch (Exception e) when (e is TimeoutException or SiloUnavailableException)
            {
                // The room can't answer (its pod is stopping: the tab was told the room is lost, and is walking back in).
                // The tab is out of its group already, and nothing renews its lease there any more: it runs out. Any
                // other failure (a live room's own rule, a bug) is a real problem with a cat still in a room nobody
                // told to leave: it must stop the move here, not be swallowed into a ghost cat.
                LogLeaveRoomFailed(logger, e, PlayerId, was);
            }
        }

        try
        {
            return await JoinRoom(place.WorldId, key, throughDoor: null);
        }
        catch (Exception e) when (e is not OperationCanceledException && !heartbeat.IsClosed(Context.ConnectionId))
        {
            // The room is full, its owner doesn't let the cat in, or it couldn't be opened: back into the room it left, and
            // told why; or, when it can't go back, into the next place on its way out (GoSomewhere).
            if (await GoSomewhere(place, key, e) is { } landed)
            {
                return landed;
            }

            throw;
        }
    }

    // Sends each of the two out of the other's home if it no longer lets them stay (they stopped being friends). The home is
    // asked who is in it (the room is the truth), unless the other's session says they're somewhere else: a home nobody is
    // in isn't opened for nothing. A session that doesn't know (its Arrived failed, say) still has the home asked.
    private async Task SendOutOfEachOthersHomes(PlayerRelation other)
    {
        // One after the other: both read the database through this call's one context.
        await SendOutOf(
            PlayerId, other.PlayerId, async () => (await players.FindAsync(PlayerId, CancellationToken.None))?.DisplayName);
        await SendOutOf(other.PlayerId, PlayerId, () => Task.FromResult<string?>(other.DisplayName));

        async Task SendOutOf(Guid owner, Guid visitor, Func<Task<string?>> ownerName)
        {
            if (await LocateAsync(visitor) is { } at && RoomKey.HomeOwner(at.WorldId, at.RoomId) != owner)
            {
                return;
            }

            await SendOutWhoMayNotStay(owner, [visitor], async () =>
                $"You and {await ownerName() ?? "that cat"} aren't friends any more, so you had to leave their home.");
        }
    }

    // Of <who> (all the cats in it, when null), sends out of <owner>'s home those its rule no longer lets stay, each told
    // <message> (made only when someone goes). Not cancelled with the tab: a change already saved must send out whoever it
    // says must go. A failure is logged: the change stays saved, and those cats stay until they leave.
    private async Task SendOutWhoMayNotStay(Guid owner, IReadOnlyList<Guid>? who, Func<Task<string>> message)
    {
        try
        {
            var room = grains.GetGrain<IRoomGrain>(RoomKey.ForHome(owner));
            var here = await room.CatsHere();
            IReadOnlyList<Guid> asked = who is null ? here : [.. here.Where(who.Contains)];
            if (asked.Count == 0)
            {
                return;
            }

            var leaving = await homes.MustLeaveAsync(owner, asked, CancellationToken.None);
            if (leaving.Count > 0)
            {
                await room.SendOut(leaving, await message());
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogSendOutFailed(logger, e, owner);
        }
    }

    // Saves a change to the player's home, then shows it to whoever is in it. Saved by then: a home that isn't told shows
    // it to whoever arrives next.
    private async Task<HomeView> Furnish(Func<Task<HomePlan>> change)
    {
        var home = HomeView.From(await PlayersRule(change));
        var key = RoomKey.ForHome(PlayerId);
        try
        {
            await grains.GetGrain<IRoomGrain>(key).Furnish(home);
        }
        catch (Exception e)
        {
            LogFurnishNotShown(logger, e, key);
        }

        return home;
    }

    /// <summary>
    /// Goes through a door of the room the cat is in, which the cat must be standing on: into the room it leads to, just
    /// inside the door there that leads back.
    /// </summary>
    public async Task<RoomSnapshot> GoThroughDoor(string doorId)
    {
        if (Here?.RoomKey is null)
        {
            throw new HubException("You aren't in a room.");
        }

        await EnsureCurrentTab();
        // Read after the wait: the heartbeat may have moved it meanwhile (a lost room or world).
        var place = Here ?? throw new HubException("You aren't in a room.");
        var key = place.RoomKey ?? throw new HubException("You aren't in a room.");

        // Out of the room, and its group, before the room lets the cat go, as in GoToRoom: no renewal meanwhile finds the
        // cat gone, and nothing the room says from here (the cat leaving included) reaches the tab loading the next room.
        if (!MoveTo(place with { RoomKey = null }))
        {
            throw new HubException(ClosedMessage);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        DoorTarget target;
        try
        {
            target = await grains.GetGrain<IRoomGrain>(key).LeaveThrough(PlayerId, Context.ConnectionId, doorId);
        }
        catch (GameRuleException e)
        {
            // Not on the door: the cat stays where it is, and so does the tab.
            await BackInto(place, key);
            throw new HubException(e.Message);
        }
        catch
        {
            // The room couldn't answer: back in it too. Had the cat gone after all, the next renewal says so, and the tab
            // walks back in.
            await BackInto(place, key);
            throw;
        }

        // A home's door leads to a room of the world the cat came from.
        var next = RoomKey.For(place.WorldId, target.RoomId);
        try
        {
            return await JoinRoom(place.WorldId, next, target.DoorId);
        }
        catch (Exception e) when (e is not OperationCanceledException && !heartbeat.IsClosed(Context.ConnectionId))
        {
            // The room past the door is full (or couldn't answer): the cat comes back in by the door it went through, and
            // is told why; but not straight back into another player's home, which may not let it in any more: then into
            // the next place on its way out (GoSomewhere).
            if (await GoSomewhere(place, next, e, doorId) is { } landed)
            {
                return landed;
            }

            throw;
        }
    }

    // Puts the connection back in the room it was leaving, which kept its cat. Had the tab closed meanwhile, its
    // disconnect found it in no room, so the cat is taken out here.
    private async Task BackInto(ConnectionPlace place, string key)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        if (!MoveTo(place))
        {
            await LeaveRoom(key);
            throw new HubException(ClosedMessage);
        }
    }

    public Task Move(double x, double z) => InRoom(room => room.Move(PlayerId, x, z));

    public async Task Say(string text)
    {
        // Muted everywhere: refused with why and until when (the browser has turned its chat off already). From this pod's
        // read of the player's standing, as for the ban: chat doesn't read the database every line.
        await PlayersRule(async () =>
        {
            await standings.EnsureMayChatAsync(PlayerId, Context.ConnectionAborted);
            return true;
        });

        var place = await InRoom(room => room.Say(PlayerId, text));
        metrics.Said(place.WorldId);
    }

    /// <summary>Sit, meow, wave or sleep: the room shows it to everyone in it.</summary>
    public async Task Emote(string emote)
    {
        var place = await InRoom(room => room.Emote(PlayerId, emote));
        metrics.Emoted(place.WorldId, emote);
    }

    /// <summary>Casts into the room's pond; the bite comes as FishBit, to the tab playing the cat then.</summary>
    public Task Cast() => InRoom(room => room.Cast(PlayerId));

    /// <summary>
    /// Reels in. A catch is paid into the player's coins first, and only once it's saved is the room shown it; a catch
    /// that can't be paid is shown to nobody, and the player is told.
    /// </summary>
    public async Task<ReelView> Reel()
    {
        var (place, key, reel) = await InRoom<ReelView>(room => room.Reel(PlayerId));
        if (reel is not { Outcome: ReelView.Caught, FishId: { } fishId, FishName: { } fishName })
        {
            return reel;
        }

        PlayerProfile paid;
        try
        {
            // Not the connection's token: a tab that closes right after reeling (a refresh) must not cancel the payment.
            paid = await players.EarnCoinsAsync(PlayerId, reel.Coins, CancellationToken.None);
        }
        catch (Exception e)
        {
            LogCatchNotPaid(logger, e, PlayerId, fishId);
            throw new HubException(CatchNotPaidMessage);
        }

        metrics.Fished(place.WorldId, fishId);
        await Clients.Group(key).CatFished(new CatchView(PlayerId, fishId, fishName, reel.Coins));
        return reel with { TotalCoins = paid.Coins };
    }

    /// <summary>Another cat's player card: who they are, and where this player stands with them.</summary>
    public async Task<PlayerCardView> GetPlayerCard(Guid playerId)
    {
        var relation = await friends.GetAsync(PlayerId, playerId, Context.ConnectionAborted)
            ?? throw new HubException("That cat doesn't exist.");
        return await CardOfAsync(relation);
    }

    /// <summary>Asks the cat to be friends (accepting, if they have asked this player); the card as it is now.</summary>
    public Task<PlayerCardView> AddFriend(Guid playerId) =>
        ChangeFriendship(playerId, friends.RequestAsync);

    public Task<PlayerCardView> AcceptFriend(Guid playerId) =>
        ChangeFriendship(playerId, friends.AcceptAsync);

    public Task<PlayerCardView> DeclineFriend(Guid playerId) =>
        ChangeFriendship(playerId, friends.DeclineAsync);

    /// <summary>Takes back this player's request to the cat.</summary>
    public Task<PlayerCardView> CancelFriendRequest(Guid playerId) =>
        ChangeFriendship(playerId, friends.CancelAsync);

    public Task<PlayerCardView> RemoveFriend(Guid playerId) =>
        ChangeFriendship(playerId, friends.UnfriendAsync, ending: true);

    /// <summary>
    /// An admin acts on another cat, from its card: <paramref name="action"/> "warn", "mute" (<paramref name="length"/>
    /// "1h", "24h" or "7d"), "ban" ("24h", "7d" or "permanent") or "lift", always saying why (<paramref name="reason"/>,
    /// which the player is shown; a warning's words). Only admins (the Domain's rule). The player is told at once
    /// (<see cref="IModerationEffects"/>); the card as it is after.
    /// </summary>
    public async Task<PlayerCardView> Moderate(Guid playerId, string action, string? reason, string? length)
    {
        var request = await PlayersRule(() => Task.FromResult(ModerationRequest.Parse(action, reason, length)));
        // Not the connection's token: a change saved must be told, even if the admin's tab closes meanwhile.
        var standing = await PlayersRule(() => moderation.ModerateAsync(PlayerId, playerId, request, CancellationToken.None));
        await moderationEffects.AppliedAsync(playerId, request.Action, standing);
        var card = await GetPlayerCard(playerId);
        if (request.Action == ModerationAction.Rename)
        {
            await NotifyRenamedAsync(playerId, card.DisplayName);
        }

        return card;
    }

    /// <summary>
    /// An admin bans networks the cat, banned now, played from lately (<paramref name="networkKeys"/>: their keys on its
    /// card), for <paramref name="length"/> ("24h" or "7d"), with the cat's ban's reason (the rules:
    /// <see cref="Domain.Moderation.AddressBan"/>). Every tab on them but an admin's is told at once, and new guests and
    /// sign-ins from them are refused; the card as it is after.
    /// </summary>
    public async Task<PlayerCardView> BanNetworks(Guid playerId, string[] networkKeys, string length)
    {
        EnsureNetworkBans();
        var span = await PlayersRule(() => Task.FromResult(ModerationRequest.NetworkLength(length)));
        // Not the connection's token: a change saved must be told, even if the admin's tab closes meanwhile.
        var banned = await PlayersRule(
            () => networks.BanAsync(PlayerId, playerId, networkKeys ?? [], span, CancellationToken.None));
        await moderationEffects.NetworksBannedAsync(banned);
        return await GetPlayerCard(playerId);
    }

    /// <summary>An admin lifts the bans in force on the networks banned with the cat (none: nothing changes); the card after.</summary>
    public async Task<PlayerCardView> LiftNetworkBans(Guid playerId)
    {
        EnsureNetworkBans();
        var lifted = await PlayersRule(() => networks.LiftOfPlayerAsync(PlayerId, playerId, CancellationToken.None));
        moderationEffects.NetworksLifted(lifted);
        return await GetPlayerCard(playerId);
    }

    /// <summary>The warnings admins left this player that they haven't seen yet, oldest first.</summary>
    public async Task<IReadOnlyList<WarningView>> GetWarnings() =>
        [.. (await moderation.WarningsAsync(PlayerId, Context.ConnectionAborted)).Select(w => new WarningView(w.Id, w.Text))];

    /// <summary>The player closed the warning <paramref name="warningId"/>: it isn't shown again (nor those before it).</summary>
    public Task SawWarning(Guid warningId) => PlayersRule(async () =>
    {
        await moderation.SawWarningAsync(PlayerId, warningId, CancellationToken.None);
        return true;
    });
    /// <summary>The players this one has muted, by name: the browser hides their chat. Asked for on every connect.</summary>
    public async Task<IReadOnlyList<MutedView>> GetMutes() =>
        Views(await mutes.ListAsync(PlayerId, Context.ConnectionAborted));

    /// <summary>Mutes the cat: this player's tabs stop showing its chat. Every tab of the player is told.</summary>
    public Task<IReadOnlyList<MutedView>> Mute(Guid playerId) =>
        ChangeMutes(() => mutes.MuteAsync(PlayerId, playerId, Context.ConnectionAborted));

    /// <summary>Unmutes the cat. Every tab of the player is told.</summary>
    public Task<IReadOnlyList<MutedView>> Unmute(Guid playerId) =>
        ChangeMutes(() => mutes.UnmuteAsync(PlayerId, playerId, Context.ConnectionAborted));

    /// <summary>
    /// Reports the cat, for review, with a reason (a <see cref="ReportReason"/>'s name) and an optional note; and, with
    /// <paramref name="alsoMute"/>, mutes it too. The report keeps the last lines said in the room this tab's cat is in,
    /// read from the room itself: nothing the browser says was said.
    /// </summary>
    public async Task<ReportReceipt> Report(Guid playerId, string reason, string? note, bool alsoMute)
    {
        // By name only: Enum.TryParse would also take numbers and lists of names.
        if (!Enum.GetNames<ReportReason>().Contains(reason, StringComparer.Ordinal))
        {
            throw new HubException("Pick a reason.");
        }

        if (Here is not { RoomKey: { } key } place)
        {
            throw new HubException("You can only report a cat from a room.");
        }

        // What was said in the room, and whether the reported cat is in it (if not, the lines are kept only if some are its).
        // Each read on its own: one that fails doesn't lose the other.
        var room = grains.GetGrain<IRoomGrain>(key);
        IReadOnlyList<ChatView> chat;
        try
        {
            chat = await room.RecentChat();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Filed without them rather than not at all: the reason and the note still say what happened.
            LogReportChatNotRead(logger, e, key);
            chat = [];
        }

        bool otherIsHere;
        try
        {
            otherIsHere = (await room.CatsHere()).Contains(playerId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Taken as not here: the lines are still kept if some are the reported cat's.
            LogReportCatsNotRead(logger, e, key);
            otherIsHere = false;
        }

        var why = Enum.Parse<ReportReason>(reason);
        var outcome = await PlayersRule(() => reports.FileAsync(
            PlayerId, playerId, why, note, place.WorldId, key, [.. chat.Select(l => new ChatLine(l.PlayerId, l.Text, l.At))],
            otherIsHere, alsoMute, Context.ConnectionAborted));
        var receipt = new ReportReceipt(outcome.Muted, Views(outcome.Mutes));
        // Only a change: the player's tabs know the list already otherwise.
        if (outcome.MuteAdded)
        {
            await TellMutes(receipt.Mutes);
        }

        return receipt;
    }

    // A change to the player's mutes; every tab of the player (this one too) is told, and hides that chat.
    private async Task<IReadOnlyList<MutedView>> ChangeMutes(Func<Task<IReadOnlyList<MutedPlayer>>> change)
    {
        var after = Views(await PlayersRule(change));
        await TellMutes(after);
        return after;
    }

    // Saved by then: a tab that isn't told gets the list on its next connect.
    private async Task TellMutes(IReadOnlyList<MutedView> after)
    {
        try
        {
            await Clients.User(PlayerUserIdProvider.For(PlayerId)).MutesChanged(after);
        }
        catch (Exception e)
        {
            LogMutesChangedNotSent(logger, e, PlayerId);
        }
    }

    private static IReadOnlyList<MutedView> Views(IReadOnlyList<MutedPlayer> muted) => [.. muted.Select(MutedView.From)];

    /// <summary>The shop: everything in it, and the player's coins, things and what their cat wears.</summary>
    public async Task<ShopView> GetShop() =>
        ShopView.From(await PlayersRule(() => shop.GetAsync(PlayerId, Context.ConnectionAborted)));

    /// <summary>
    /// Buys something with the player's coins (it isn't put on), from the catalogue of the room this tab's cat is in:
    /// the shop's, or the purchase is refused (a rule of the Domain); the shop as it is after.
    /// </summary>
    public async Task<ShopView> Buy(string itemId)
    {
        // Only the tab that plays the cat: a replaced one may still think it is in the shop. (In no room, it buys nothing
        // anyway.)
        if (Here?.RoomKey is not null)
        {
            await EnsureCurrentTab();
        }

        // Read after the wait. A home's room is its owner's ID, which is no room of the catalogue: it sells nothing.
        var roomId = Here?.RoomKey is { } key ? RoomKey.Parse(key).RoomId : null;
        return ShopView.From(await PlayersRule(() => shop.BuyAsync(PlayerId, roomId, itemId, Context.ConnectionAborted)));
    }

    /// <summary>Puts on a hat, collar or fur the player has: the room the cat is in sees it at once.</summary>
    public Task<ShopView> Wear(string itemId) => Dress(() => shop.WearAsync(PlayerId, itemId, Context.ConnectionAborted));

    /// <summary>Takes off the hat or collar the cat wears: the room the cat is in sees it at once.</summary>
    public Task<ShopView> TakeOff(string itemId) =>
        Dress(() => shop.TakeOffAsync(PlayerId, itemId, Context.ConnectionAborted));

    /// <summary>
    /// Renames this player's own cat, from their own card: the rules (length, the chat filter's lists, no staff
    /// impersonation, at most once a day unless the player is an admin) are <see cref="Domain.Players.Player.Rename"/>'s.
    /// Shown at once: the cat's label wherever its room is, this player's other tabs, their friends' lists, and their
    /// home's name. Returns the new name, as the server has it.
    /// </summary>
    public async Task<string> Rename(string displayName)
    {
        // Not the connection's token: a change saved must be shown, even if this tab's connection closes meanwhile.
        var profile = await PlayersRule(() => players.RenameAsync(PlayerId, displayName, CancellationToken.None));
        await NotifyRenamedAsync(PlayerId, profile.DisplayName);
        return profile.DisplayName;
    }

    /// <summary>
    /// The player's friends and requests, each friend with where they are now: their session says which room, and that
    /// room confirms the cat is there. A friend between rooms or offline has no place.
    /// </summary>
    public async Task<IReadOnlyList<FriendView>> GetFriends()
    {
        var list = await friends.ListAsync(PlayerId, Context.ConnectionAborted);
        var places = await Task.WhenAll(list.Select(friend => friend.State == FriendState.Friends
            ? LocateAsync(friend.PlayerId)
            : Task.FromResult<PlayerPlace?>(null)));
        var open = await homes.OpenToAsync(
            PlayerId, [.. list.Where(friend => friend.State == FriendState.Friends).Select(friend => friend.PlayerId)],
            Context.ConnectionAborted);
        var names = list.ToDictionary<PlayerRelation, Guid, string?>(friend => friend.PlayerId, friend => friend.DisplayName);
        var views = new List<FriendView>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var friend = list[i];
            views.Add(new FriendView(
                friend.PlayerId, friend.DisplayName, friend.FurColor.ToString(), friend.State.ToString(),
                await PlaceViewAsync(places[i], names), open.Contains(friend.PlayerId)));
        }

        return views;
    }

    /// <summary>
    /// Goes to the friend's room, in whichever world they are, within the world's and the room's caps. Into another world,
    /// the player takes a place there first, so a full world leaves them where they were.
    /// </summary>
    public async Task<RoomSnapshot> GoToFriend(Guid friendId)
    {
        if (Here is null)
        {
            throw new HubException("Pick a world first.");
        }

        await EnsureCurrentTab();
        var relation = await PlayersRule(() => friends.VisitAsync(PlayerId, friendId, Context.ConnectionAborted));

        var target = await LocateAsync(friendId)
            ?? throw new HubException($"{relation.DisplayName} isn't in a room right now.");
        // Read after the waits: the heartbeat may have moved it meanwhile (a lost room or world).
        var place = Here ?? throw new HubException("Pick a world first.");
        if (place.RoomKey == RoomKey.For(target.WorldId, target.RoomId))
        {
            throw new HubException($"You're already with {relation.DisplayName}.");
        }

        // A friend in a home: that home, if its owner lets this player in (the friend's, or another's).
        if (RoomKey.HomeOwner(target.WorldId, target.RoomId) is { } owner)
        {
            return await VisitHome(owner);
        }

        if (target.WorldId == place.WorldId)
        {
            return await GoToRoom(target.RoomId);
        }

        var world = WorldCatalog.Find(target.WorldId) ?? throw new HubException("That world doesn't exist.");
        var worldGrain = grains.GetGrain<IWorldGrain>(world.Id);
        if (!await worldGrain.TryEnter(PlayerId, Context.ConnectionId))
        {
            throw new HubException($"{world.Name} is full. Try again later.");
        }

        RoomSnapshot snapshot;
        try
        {
            snapshot = await JoinRoom(world.Id, RoomKey.For(world.Id, target.RoomId), throughDoor: null);
        }
        catch
        {
            await worldGrain.Leave(PlayerId, Context.ConnectionId);
            throw;
        }

        metrics.EnteredWorld(world.Id);
        // Out of the room and world it was in, now that it's in the new ones. A leave that fails falls back to the leases
        // running out: nothing renews them any more.
        try
        {
            await Leave(place);
        }
        catch (Exception e)
        {
            LogLeaveFailed(logger, e, PlayerId, place.WorldId);
        }

        return snapshot;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Forgotten first, whatever happens next: a leave that fails falls back to the leases running out.
        var place = heartbeat.Close(Context.ConnectionId);
        try
        {
            await Leave(place);
        }
        finally
        {
            await Session.Release(Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Guid PlayerId => Context.User?.GetPlayerId() ?? throw new HubException("Sign in first.");

    private IPlayerSessionGrain Session => grains.GetGrain<IPlayerSessionGrain>(PlayerId);

    private ConnectionPlace? Here => heartbeat.PlaceOf(Context.ConnectionId);

    // The one way the connection's place changes, so what the hub reads and what the heartbeat renews can't differ.
    // False once the connection has closed: the caller undoes what it did.
    private bool MoveTo(ConnectionPlace? place) => heartbeat.Track(Context.ConnectionId, place);

    private async Task EnsureCurrentTab()
    {
        if (!await Session.IsCurrent(Context.ConnectionId))
        {
            throw new HubException(ReplacedMessage);
        }
    }

    // Into the room <key> (a room of <worldId>, or a home, which the cat visits from that world).
    private async Task<RoomSnapshot> JoinRoom(string worldId, string key, string? throughDoor)
    {
        if (heartbeat.IsClosed(Context.ConnectionId))
        {
            throw new HubException(ClosedMessage);
        }

        var profile = await players.FindAsync(PlayerId, Context.ConnectionAborted)
            ?? throw new HubException("Your cat wasn't found. Sign in again.");
        var room = grains.GetGrain<IRoomGrain>(key);

        // In the group before joining, so nothing said in between is missed; the browser treats repeats as updates.
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        RoomSnapshot snapshot;
        try
        {
            var card = new PlayerCard(
                profile.Id, profile.DisplayName, profile.FurColor.ToString(), profile.Hat, profile.Collar, profile.OutfitVersion);
            snapshot = await room.Join(card, Context.ConnectionId, throughDoor);
        }
        catch (GameRuleException e)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
            throw new HubException(e.Message);
        }
        catch
        {
            // Couldn't answer: out of its group too, or the tab would hear that room wherever the cat goes next.
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
            throw;
        }

        if (!MoveTo(new ConnectionPlace(PlayerId, worldId, key)))
        {
            await room.Leave(PlayerId, Context.ConnectionId);
            throw new HubException(ClosedMessage);
        }

        // Where friends find the cat. Only a pointer, which the room confirms: a failure here only hides the cat from them.
        try
        {
            await Session.Arrived(Context.ConnectionId, key);
        }
        catch (Exception e)
        {
            LogArrivalNotRecorded(logger, e, PlayerId, key);
        }

        // A home is in no world of its own: the cat is still in the one it came from.
        return snapshot is { Home: not null } ? snapshot with { WorldId = worldId } : snapshot;
    }

    // A change to the friendship with another cat, as this player; the other player's tabs are told.
    private async Task<PlayerCardView> ChangeFriendship(
        Guid otherId, Func<Guid, Guid, CancellationToken, Task<PlayerRelation>> change, bool ending = false)
    {
        var relation = await PlayersRule(() => change(PlayerId, otherId, Context.ConnectionAborted));
        if (ending)
        {
            // Friends no more: a friends-only home no longer lets either stay. (Answering a request changes no home.)
            await SendOutOfEachOthersHomes(relation);
        }

        // Saved by now: a tab that isn't told sees it the next time it opens the list or the card.
        try
        {
            await Clients.User(PlayerUserIdProvider.For(otherId)).FriendsChanged(PlayerId);
        }
        catch (Exception e)
        {
            LogFriendsChangedNotSent(logger, e, otherId);
        }

        return await CardOfAsync(relation);
    }

    // The card of another cat, with whether its home would let this player in now; for an admin, with its standing too.
    private async Task<PlayerCardView> CardOfAsync(PlayerRelation relation)
    {
        var open = await homes.OpenToAsync(PlayerId, [relation.PlayerId], Context.ConnectionAborted);
        CardModerationView? standing = null;
        if (moderation.IsAdmin(PlayerId))
        {
            var now = await moderation.StandingAsync(relation.PlayerId, Context.ConnectionAborted);
            // The networks it played from lately, with how many players each: not for another admin (not moderated).
            var seenOn = addresses.Enabled && !moderation.IsAdmin(relation.PlayerId)
                ? CardModerationView.NetworksOf(
                    await networks.RecentOfAsync(PlayerId, relation.PlayerId, Context.ConnectionAborted))
                : null;
            standing = new CardModerationView(
                moderation.IsAdmin(relation.PlayerId), SanctionView.From(now.Ban), SanctionView.From(now.Mute), seenOn);
        }

        return new(relation.PlayerId, relation.DisplayName, relation.FurColor.ToString(), relation.State.ToString(),
            ShopCatalog.Find(relation.Hat)?.Name, ShopCatalog.Find(relation.Collar)?.Name, open.Contains(relation.PlayerId),
            relation.Hat, relation.Collar, standing);
    }

    // Network bans are off without the server's key: nothing to ban networks with.
    private void EnsureNetworkBans()
    {
        if (!addresses.Enabled)
        {
            throw new HubException("Network bans are off on this server.");
        }
    }

    // A use case's call: a rule it breaks (a DomainException) is the player's message.
    private static async Task<T> PlayersRule<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (DomainException e)
        {
            throw new HubException(e.Message);
        }
    }

    // Saves a change to what the cat wears, then shows it in the room the cat is in: where the player's current tab has
    // it (any tab can dress the cat), or failing that this tab's room. Saved by then: a room that isn't told shows it to
    // whoever arrives next.
    private async Task<ShopView> Dress(Func<Task<Wardrobe>> change)
    {
        var wardrobe = await PlayersRule(change);
        var key = await LocateAsync(PlayerId) is { } place ? RoomKey.For(place.WorldId, place.RoomId) : Here?.RoomKey;

        if (key is not null)
        {
            try
            {
                await grains.GetGrain<IRoomGrain>(key).Dress(OutfitView.From(wardrobe));
            }
            catch (Exception e)
            {
                LogDressNotShown(logger, e, PlayerId, key);
            }
        }

        return ShopView.From(wardrobe);
    }

    // Where the player's cat is, or null when it can't be told (a grain call that failed counts as offline).
    private async Task<PlayerPlace?> LocateAsync(Guid playerId)
    {
        try
        {
            return await grains.GetGrain<IPlayerSessionGrain>(playerId).Locate();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogLocateFailed(logger, e, playerId);
            return null;
        }
    }

    // Shows a rename everywhere at once: the cat's label in whatever room it's in now (and its home's name, told
    // separately even when the player isn't in it: a friend visiting sees it too), this player's other tabs, and their
    // friends' lists (the existing FriendsChanged path). Already saved by now: each notification here is best-effort,
    // and a tab that misses one shows the new name anyway at its next read (a room joined, a card opened, a list opened).
    private async Task NotifyRenamedAsync(Guid playerId, string displayName)
    {
        // Renaming yourself: as Dress does, fall back to this tab's own tracked place when the session's pointer to it
        // hasn't caught up yet. An admin renaming another cat has no "this tab" of theirs to fall back to.
        var current = await LocateAsync(playerId);
        var currentKey = current is { } place
            ? RoomKey.For(place.WorldId, place.RoomId)
            : playerId == PlayerId ? Here?.RoomKey : null;
        var keys = new HashSet<string>(StringComparer.Ordinal) { RoomKey.ForHome(playerId) };
        if (currentKey is not null)
        {
            keys.Add(currentKey);
        }

        foreach (var key in keys)
        {
            try
            {
                await grains.GetGrain<IRoomGrain>(key).Rename(playerId, displayName);
            }
            catch (Exception e)
            {
                LogRenameNotShown(logger, e, playerId, key);
            }
        }

        try
        {
            await Clients.User(PlayerUserIdProvider.For(playerId)).Renamed(displayName);
        }
        catch (Exception e)
        {
            LogRenamedNotSent(logger, e, playerId);
        }

        try
        {
            var list = await friends.ListAsync(playerId, CancellationToken.None);
            foreach (var friend in list.Where(f => f.State == FriendState.Friends))
            {
                try
                {
                    await Clients.User(PlayerUserIdProvider.For(friend.PlayerId)).FriendsChanged(playerId);
                }
                catch (Exception e)
                {
                    LogFriendsChangedNotSent(logger, e, friend.PlayerId);
                }
            }
        }
        catch (Exception e)
        {
            LogFriendsListNotRead(logger, e, playerId);
        }
    }

    // Where a friend is, with the names to show: a home by its owner's name (from <names>, the friends list and the names
    // read so far). One at a time: a name not in it is read from the database, and kept in it.
    private async Task<FriendPlaceView?> PlaceViewAsync(PlayerPlace? place, Dictionary<Guid, string?> names)
    {
        if (place is not null && RoomKey.HomeOwner(place.WorldId, place.RoomId) is { } owner)
        {
            if (owner == PlayerId)
            {
                return new FriendPlaceView("", "", Home.Layout.Id, "Your home", owner);
            }

            // Read once per list: several friends can be in the same home.
            if (!names.TryGetValue(owner, out var name))
            {
                name = names[owner] = (await players.FindAsync(owner, Context.ConnectionAborted))?.DisplayName;
            }

            return name is null ? null : new FriendPlaceView("", "", Home.Layout.Id, Home.NameOf(name), owner);
        }

        return WorldPlaceView(place);
    }

    private FriendPlaceView? WorldPlaceView(PlayerPlace? place) =>
        place is not null && WorldCatalog.Find(place.WorldId) is { } world && rooms.Find(place.RoomId) is { } room
            ? new FriendPlaceView(world.Id, world.Name, room.Id, room.Name)
            : null;

    private async Task LeaveRoom(string key)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        await grains.GetGrain<IRoomGrain>(key).Leave(PlayerId, Context.ConnectionId);
    }

    // Leaves the room and the world the connection was in. The world is left even when leaving the room fails.
    private async Task Leave(ConnectionPlace? place)
    {
        if (place is null)
        {
            return;
        }

        try
        {
            if (place.RoomKey is { } key)
            {
                await LeaveRoom(key);
            }
        }
        finally
        {
            metrics.LeftWorld(place.WorldId);
            await grains.GetGrain<IWorldGrain>(place.WorldId).Leave(PlayerId, Context.ConnectionId);
        }
    }

    private async Task<ConnectionPlace> InRoom(Func<IRoomGrain, Task> action)
    {
        var (place, _, _) = await InRoom<bool>(async room =>
        {
            await action(room);
            return true;
        });
        return place;
    }

    // Calls the room the connection is in, as the player's current tab; a rule the call breaks is the player's message.
    private async Task<(ConnectionPlace Place, string RoomKey, T Result)> InRoom<T>(Func<IRoomGrain, Task<T>> action)
    {
        var place = Here ?? throw new HubException("You aren't in a room.");
        var key = place.RoomKey ?? throw new HubException("You aren't in a room.");
        await EnsureCurrentTab();
        try
        {
            return (place, key, await action(grains.GetGrain<IRoomGrain>(key)));
        }
        catch (GameRuleException e)
        {
            throw new HubException(e.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player {PlayerId} couldn't leave {RoomKey} on the way to another room; the lease there runs out instead")]
    private static partial void LogLeaveRoomFailed(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't record that player {PlayerId} is in {RoomKey}; friends won't find them there")]
    private static partial void LogArrivalNotRecorded(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't show the room {RoomKey} what player {PlayerId} put on; it shows on their next arrival")]
    private static partial void LogDressNotShown(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player {PlayerId} couldn't go into {RoomKey} on their way out")]
    private static partial void LogNotLandedIn(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Player {PlayerId} was refused {RoomKey} on their way out")]
    private static partial void LogRoomRefused(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nothing answered for player {PlayerId} within the way-out time limit, trying {RoomKey}")]
    private static partial void LogWayOutTimedOut(ILogger logger, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't leave {RoomKey} for player {PlayerId} after its join failed; a ghost may stay until its lease runs out")]
    private static partial void LogLeaveAfterFailedJoinFailed(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send an ex-friend out of the home of player {PlayerId}; they stay until they leave")]
    private static partial void LogSendOutFailed(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't show the home {RoomKey} its owner's change; it shows on the next arrival")]
    private static partial void LogFurnishNotShown(ILogger logger, Exception exception, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId} their friendships changed")]
    private static partial void LogFriendsChangedNotSent(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't show the room {RoomKey} that player {PlayerId} was renamed; it shows on the next arrival")]
    private static partial void LogRenameNotShown(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId}'s other tabs they were renamed")]
    private static partial void LogRenamedNotSent(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read player {PlayerId}'s friends to tell them of a rename")]
    private static partial void LogFriendsListNotRead(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId}'s tabs their mutes changed")]
    private static partial void LogMutesChangedNotSent(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the chat of {RoomKey} for a report; filed without it")]
    private static partial void LogReportChatNotRead(ILogger logger, Exception exception, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read who is in {RoomKey} for a report; the reported cat is taken as not there")]
    private static partial void LogReportCatsNotRead(ILogger logger, Exception exception, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't find where player {PlayerId} is; shown offline")]
    private static partial void LogLocateFailed(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player {PlayerId} went to a friend but couldn't leave {WorldId}; its leases will run out")]
    private static partial void LogLeaveFailed(ILogger logger, Exception exception, Guid playerId, string worldId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't pay player {PlayerId} for a {FishId}; the catch wasn't shown")]
    private static partial void LogCatchNotPaid(ILogger logger, Exception exception, Guid playerId, string fishId);
}
