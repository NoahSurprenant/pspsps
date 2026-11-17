using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pspsps.Application.Friends;
using Pspsps.Application.Homes;
using Pspsps.Application.Players;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;
using Pspsps.Domain.Worlds;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
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
    IHomeService homes,
    SessionHeartbeat heartbeat,
    GameMetrics metrics,
    ILogger<GameHub> logger)
    : Hub<IGameClient>
{
    /// <summary>What a replaced tab is told; the client shows the same words.</summary>
    public const string ReplacedMessage = "You've joined from another tab.";

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
        return await JoinRoom(world.Id, RoomKey.For(world.Id, RoomCatalog.Default.Id), throughDoor: null);
    }

    public async Task<RoomSnapshot> GoToRoom(string roomId)
    {
        if (Here is null)
        {
            throw new HubException("Pick a world first.");
        }

        await EnsureCurrentTab();
        var room = RoomCatalog.Find(roomId) ?? throw new HubException("That room doesn't exist.");
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
        var plan = await PlayersRule(() => homes.VisitAsync(PlayerId, ownerId, Context.ConnectionAborted));
        // Read after the waits: the heartbeat may have moved it meanwhile (a lost room or world).
        var place = Here ?? throw new HubException("Pick a world first.");
        var key = RoomKey.ForHome(ownerId);
        if (place.RoomKey == key)
        {
            throw new HubException(ownerId == PlayerId ? "You're home already." : $"You're in {plan.OwnerName}'s home already.");
        }

        var snapshot = await MoveInto(place, key);
        if (ownerId == PlayerId)
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
            await BackOut(place, key);
            throw new HubException(e.Message);
        }
        catch
        {
            // Couldn't be checked: not let in, rather than in a home the browser doesn't know it is in.
            await BackOut(place, key);
            throw;
        }

        return snapshot;
    }

    // Takes the cat out of the home <key> it may not stay in, back into the room it came from (if any).
    private async Task BackOut(ConnectionPlace from, string key)
    {
        var now = Here;
        if (now?.RoomKey != key || !MoveTo(now with { RoomKey = null }))
        {
            return;
        }

        await LeaveRoom(key);
        await GoBackInto(from, key);
    }

    // Back into the room <from> the cat just left, unless it was another room <than> or a home that no longer lets the cat
    // in. A room that can't take it back (it filled up meanwhile) leaves it in none: the browser keeps the room it showed,
    // and the next renewal's RoomLost walks it back in.
    private async Task GoBackInto(ConnectionPlace from, string than)
    {
        if (from.RoomKey is not { } was || was == than || heartbeat.IsClosed(Context.ConnectionId))
        {
            return;
        }

        try
        {
            if (RoomKey.HomeOwner(was) is { } owner && owner != PlayerId)
            {
                await homes.VisitAsync(PlayerId, owner, CancellationToken.None);
            }

            await JoinRoom(from.WorldId, was, throughDoor: null);
        }
        catch (Exception e) when (e is DomainException or HubException)
        {
            LogNotBackIn(logger, e, PlayerId, was);
            // Renewing a room it isn't in tells the tab, at the next renewal, to walk back in.
            MoveTo(from);
        }
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
    /// rule) are sent out, back to their own world's Town Square, and told why.
    /// </summary>
    public async Task<HomeView> LetIntoHome(string visitors)
    {
        if (!Enum.TryParse<HomeVisitors>(visitors, out var choice) || !Enum.IsDefined(choice))
        {
            throw new HubException("That isn't a choice.");
        }

        var home = await Furnish(() => homes.LetInAsync(PlayerId, choice, Context.ConnectionAborted));
        await SendOutWhoMayNotStay(
            PlayerId, null, $"{home.OwnerName} changed who can visit their home, so you went back to Town Square.");

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

        if (place.RoomKey is { } was)
        {
            await LeaveRoom(was);
        }

        try
        {
            return await JoinRoom(place.WorldId, key, throughDoor: null);
        }
        catch (HubException) when (!heartbeat.IsClosed(Context.ConnectionId))
        {
            // The room is full (or its owner doesn't let the cat in): back into the room it left, and told why.
            await GoBackInto(place, key);
            throw;
        }
    }

    // Sends each of the two out of the other's home if it no longer lets them stay (they stopped being friends). Only a
    // home the other is in, as their session says, is asked: the room then has the last word.
    private async Task SendOutOfEachOthersHomes(PlayerRelation other)
    {
        foreach (var (owner, visitor, ownerName) in new[]
        {
            (PlayerId, other.PlayerId, (string?)null),
            (other.PlayerId, PlayerId, other.DisplayName),
        })
        {
            if (await LocateAsync(visitor) is not { } place || RoomKey.HomeOwner(RoomKey.For(place.WorldId, place.RoomId)) != owner)
            {
                continue;
            }

            try
            {
                var name = ownerName ?? (await players.FindAsync(owner, CancellationToken.None))?.DisplayName ?? "That cat";
                await SendOutWhoMayNotStay(
                    owner, [visitor], $"You and {name} aren't friends any more, so you went back to Town Square.");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogSendOutFailed(logger, e, owner);
            }
        }
    }

    // Of <who> (all the cats in it, when null), sends out of <owner>'s home those its rule no longer lets stay, each told
    // <message>. Not cancelled with the tab: a change already saved must send out whoever it says must go.
    private async Task SendOutWhoMayNotStay(Guid owner, IReadOnlyList<Guid>? who, string message)
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
            await room.SendOut(leaving, message);
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

        try
        {
            // A home's door leads to a room of the world the cat came from.
            return await JoinRoom(place.WorldId, RoomKey.For(place.WorldId, target.RoomId), target.DoorId);
        }
        catch (HubException) when (!heartbeat.IsClosed(Context.ConnectionId))
        {
            // The room past the door is full: the cat comes back in by the door it went through, and is told why.
            await JoinRoom(place.WorldId, key, doorId);
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
        ChangeFriendship(playerId, friends.UnfriendAsync);

    /// <summary>The shop: everything in it, and the player's coins, things and what their cat wears.</summary>
    public async Task<ShopView> GetShop() =>
        ShopView.From(await PlayersRule(() => shop.GetAsync(PlayerId, Context.ConnectionAborted)));

    /// <summary>Buys something with the player's coins (it isn't put on); the shop as it is after.</summary>
    public async Task<ShopView> Buy(string itemId) =>
        ShopView.From(await PlayersRule(() => shop.BuyAsync(PlayerId, itemId, Context.ConnectionAborted)));

    /// <summary>Puts on a hat, collar or fur the player has: the room the cat is in sees it at once.</summary>
    public Task<ShopView> Wear(string itemId) => Dress(() => shop.WearAsync(PlayerId, itemId, Context.ConnectionAborted));

    /// <summary>Takes off the hat or collar the cat wears: the room the cat is in sees it at once.</summary>
    public Task<ShopView> TakeOff(string itemId) =>
        Dress(() => shop.TakeOffAsync(PlayerId, itemId, Context.ConnectionAborted));

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
        if (RoomKey.HomeOwner(RoomKey.For(target.WorldId, target.RoomId)) is { } owner)
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
        Guid otherId, Func<Guid, Guid, CancellationToken, Task<PlayerRelation>> change)
    {
        var relation = await PlayersRule(() => change(PlayerId, otherId, Context.ConnectionAborted));
        if (relation.State == FriendState.None)
        {
            // Not friends (any more): a friends-only home no longer lets either stay.
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

    // The card of another cat, with whether its home would let this player in now.
    private async Task<PlayerCardView> CardOfAsync(PlayerRelation relation)
    {
        var open = await homes.OpenToAsync(PlayerId, [relation.PlayerId], Context.ConnectionAborted);
        return new(relation.PlayerId, relation.DisplayName, relation.FurColor.ToString(), relation.State.ToString(),
            ShopCatalog.Find(relation.Hat)?.Name, ShopCatalog.Find(relation.Collar)?.Name, open.Contains(relation.PlayerId));
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

    // Where a friend is, with the names to show: a home by its owner's name (from <names>, the friends list and the names
    // read so far). One at a time: a name not in it is read from the database, and kept in it.
    private async Task<FriendPlaceView?> PlaceViewAsync(PlayerPlace? place, Dictionary<Guid, string?> names)
    {
        if (place is not null && RoomKey.HomeOwner(RoomKey.For(place.WorldId, place.RoomId)) is { } owner)
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

    private static FriendPlaceView? WorldPlaceView(PlayerPlace? place) =>
        place is not null && WorldCatalog.Find(place.WorldId) is { } world && RoomCatalog.Find(place.RoomId) is { } room
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't record that player {PlayerId} is in {RoomKey}; friends won't find them there")]
    private static partial void LogArrivalNotRecorded(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't show the room {RoomKey} what player {PlayerId} put on; it shows on their next arrival")]
    private static partial void LogDressNotShown(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player {PlayerId} couldn't go back into {RoomKey}; the next renewal walks them back in")]
    private static partial void LogNotBackIn(ILogger logger, Exception exception, Guid playerId, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send an ex-friend out of the home of player {PlayerId}; they stay until they leave")]
    private static partial void LogSendOutFailed(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't show the home {RoomKey} its owner's change; it shows on the next arrival")]
    private static partial void LogFurnishNotShown(ILogger logger, Exception exception, string roomKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId} their friendships changed")]
    private static partial void LogFriendsChangedNotSent(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't find where player {PlayerId} is; shown offline")]
    private static partial void LogLocateFailed(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player {PlayerId} went to a friend but couldn't leave {WorldId}; its leases will run out")]
    private static partial void LogLeaveFailed(ILogger logger, Exception exception, Guid playerId, string worldId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't pay player {PlayerId} for a {FishId}; the catch wasn't shown")]
    private static partial void LogCatchNotPaid(ILogger logger, Exception exception, Guid playerId, string fishId);
}
