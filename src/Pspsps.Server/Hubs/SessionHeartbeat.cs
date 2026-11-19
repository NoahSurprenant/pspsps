using Microsoft.AspNetCore.SignalR;
using Pspsps.Domain;
using Pspsps.Domain.Worlds;
using Pspsps.Server.Game;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Hubs;

/// <summary>Where a connection is: in a world, and maybe one of its rooms (by <see cref="RoomKey"/>).</summary>
public sealed record ConnectionPlace(Guid PlayerId, string WorldId, string? RoomKey);

/// <summary>
/// Where each of this pod's connections is (the hub's one record of it), and the renewal of their leases with their
/// world and room grains every <see cref="SessionLease.RenewEvery"/>, idle or not, for as long as each is open here. A
/// tab left open keeps its cat; when this pod dies without closing its connections, nothing renews them, so their cats
/// leave their rooms once the leases run out. One call per world and per room, not per connection.
/// </summary>
public sealed partial class SessionHeartbeat(
    IGrainFactory grains,
    IHubContext<GameHub, IGameClient> hub,
    GameMetrics metrics,
    TimeProvider time,
    ILogger<SessionHeartbeat> logger) : BackgroundService
{
    // Closed connections are remembered this long, so a hub call that finishes after its connection closed can't track
    // it again. Hub calls give up long before.
    private static readonly TimeSpan RememberClosed = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ConnectionPlace> _places = [];
    private readonly Dictionary<string, DateTimeOffset> _closed = [];

    /// <summary>Where <paramref name="connectionId"/> is; null when it isn't in a world.</summary>
    public ConnectionPlace? PlaceOf(string connectionId)
    {
        lock (_gate)
        {
            return _places.GetValueOrDefault(connectionId);
        }
    }

    /// <summary>
    /// Records where <paramref name="connectionId"/> is now (null: nowhere). False, and nothing recorded, once the
    /// connection has closed.
    /// </summary>
    public bool Track(string connectionId, ConnectionPlace? place)
    {
        lock (_gate)
        {
            if (_closed.ContainsKey(connectionId))
            {
                return false;
            }

            if (place is null)
            {
                _places.Remove(connectionId);
            }
            else
            {
                _places[connectionId] = place;
            }

            return true;
        }
    }

    /// <summary>Whether <paramref name="connectionId"/> has closed.</summary>
    public bool IsClosed(string connectionId)
    {
        lock (_gate)
        {
            return _closed.ContainsKey(connectionId);
        }
    }

    /// <summary>
    /// Forgets <paramref name="connectionId"/> for good (it closed) and returns where it was, for the hub to leave.
    /// </summary>
    public ConnectionPlace? Close(string connectionId)
    {
        lock (_gate)
        {
            _closed[connectionId] = time.GetUtcNow();
            _places.Remove(connectionId, out var place);
            return place;
        }
    }

    /// <summary>
    /// Renews every connection's leases now. A tab whose cat its room no longer has leaves that room and is told to walk
    /// back in; a tab its world no longer counts and can't take back (full) leaves the world and is told to pick one.
    /// </summary>
    public async Task RenewAsync()
    {
        KeyValuePair<string, ConnectionPlace>[] connections;
        lock (_gate)
        {
            connections = [.. _places];
            var forgetBefore = time.GetUtcNow() - RememberClosed;
            foreach (var old in _closed.Where(c => c.Value < forgetBefore).Select(c => c.Key).ToList())
            {
                _closed.Remove(old);
            }
        }

        var worlds = connections
            .GroupBy(c => c.Value.WorldId)
            .Select(world => RenewWorld(world.Key, Sessions(world)));
        var rooms = connections
            .Where(c => c.Value.RoomKey is not null)
            .GroupBy(c => c.Value.RoomKey!)
            .Select(room => RenewRoom(room.Key, Sessions(room)));
        await Task.WhenAll(worlds.Concat(rooms));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SessionLease.RenewEvery, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RenewAsync();
            }
            catch (Exception e)
            {
                // The next round tries again; a lease outlasts two missed ones.
                LogRenewFailed(logger, e);
            }
        }
    }

    private async Task RenewWorld(string worldId, List<PlayerSession> sessions)
    {
        var refused = await grains.GetGrain<IWorldGrain>(worldId).Renew(sessions);
        var name = WorldCatalog.Find(worldId)?.Name ?? worldId;
        await Task.WhenAll(refused
            .Select(connectionId => (connectionId, place: LeaveLostWorld(connectionId, worldId)))
            .Where(lost => lost.place is not null)
            .Select(lost => TellWorldLost(lost.connectionId, lost.place!, name)));
    }

    private async Task TellWorldLost(string connectionId, ConnectionPlace place, string worldName)
    {
        metrics.LeftWorld(place.WorldId);
        if (place.RoomKey is { } roomKey)
        {
            await HubGroups.RemoveAsync(hub, connectionId, roomKey, time);
            await grains.GetGrain<IRoomGrain>(roomKey).Leave(place.PlayerId, connectionId);
        }

        await hub.Clients.Client(connectionId).WorldLost($"{worldName} filled up while the game recovered. Pick a world.");
    }

    private async Task RenewRoom(string roomKey, List<PlayerSession> sessions)
    {
        var lost = await grains.GetGrain<IRoomGrain>(roomKey).Renew(sessions);
        await Task.WhenAll(lost.Where(connectionId => LeaveLostRoom(connectionId, roomKey)).Select(async connectionId =>
        {
            // Out of the room's group too, so the tab hears nothing more from a room it isn't in.
            await HubGroups.RemoveAsync(hub, connectionId, roomKey, time);
            await hub.Clients.Client(connectionId).RoomLost();
        }));
    }

    // Takes the connection out of a world that no longer counts it, unless it has moved on meanwhile; returns where it was.
    private ConnectionPlace? LeaveLostWorld(string connectionId, string worldId)
    {
        lock (_gate)
        {
            if (!_places.TryGetValue(connectionId, out var place) || place.WorldId != worldId)
            {
                return null;
            }

            _places.Remove(connectionId);
            return place;
        }
    }

    // Takes the connection out of a room that no longer has its cat, unless it has moved on meanwhile.
    private bool LeaveLostRoom(string connectionId, string roomKey)
    {
        lock (_gate)
        {
            if (!_places.TryGetValue(connectionId, out var place) || place.RoomKey != roomKey)
            {
                return false;
            }

            _places[connectionId] = place with { RoomKey = null };
            return true;
        }
    }

    private static List<PlayerSession> Sessions(IEnumerable<KeyValuePair<string, ConnectionPlace>> connections) =>
        [.. connections.Select(c => new PlayerSession(c.Value.PlayerId, c.Key))];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't renew this pod's session leases")]
    private static partial void LogRenewFailed(ILogger logger, Exception exception);
}
