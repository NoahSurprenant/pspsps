using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pspsps.Application.Players;
using Pspsps.Domain.Rooms;
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
    IGrainFactory grains, IPlayerService players, SessionHeartbeat heartbeat, GameMetrics metrics, ILogger<GameHub> logger)
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
        return await JoinRoom(world.Id, RoomCatalog.Default.Id);
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
        if (!MoveTo(place with { RoomKey = null }))
        {
            // The tab closed meanwhile; the disconnect leaves the room it was in.
            throw new HubException(ClosedMessage);
        }

        if (place.RoomKey is { } key)
        {
            await LeaveRoom(key);
        }

        return await JoinRoom(place.WorldId, room.Id);
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

    private async Task<RoomSnapshot> JoinRoom(string worldId, string roomId)
    {
        if (heartbeat.IsClosed(Context.ConnectionId))
        {
            throw new HubException(ClosedMessage);
        }

        var profile = await players.FindAsync(PlayerId, Context.ConnectionAborted)
            ?? throw new HubException("Your cat wasn't found. Sign in again.");
        var key = RoomKey.For(worldId, roomId);
        var room = grains.GetGrain<IRoomGrain>(key);

        // In the group before joining, so nothing said in between is missed; the browser treats repeats as updates.
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        RoomSnapshot snapshot;
        try
        {
            var card = new PlayerCard(profile.Id, profile.DisplayName, profile.FurColor.ToString());
            snapshot = await room.Join(card, Context.ConnectionId);
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

        return snapshot;
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't pay player {PlayerId} for a {FishId}; the catch wasn't shown")]
    private static partial void LogCatchNotPaid(ILogger logger, Exception exception, Guid playerId, string fishId);
}
