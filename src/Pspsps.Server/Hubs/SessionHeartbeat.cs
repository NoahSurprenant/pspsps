using System.Collections.Concurrent;
using Pspsps.Domain;
using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>
/// Renews the leases of this pod's connections with their world and room grains every
/// <see cref="SessionLease.RenewEvery"/>, idle or not, for as long as each is open here: a tab left open keeps its cat,
/// and when this pod dies without closing its connections, nothing renews them, so their cats leave their rooms once the
/// leases run out. One call per world and per room, not per connection.
/// </summary>
public sealed partial class SessionHeartbeat(IGrainFactory grains, TimeProvider time, ILogger<SessionHeartbeat> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<string, Place> _connections = new();

    /// <summary>
    /// Records where <paramref name="connectionId"/> is now: in a world and maybe one of its rooms, or (no world) nowhere.
    /// </summary>
    public void Track(string connectionId, Guid playerId, string? worldId, string? roomKey)
    {
        if (worldId is null)
        {
            _connections.TryRemove(connectionId, out _);
        }
        else
        {
            _connections[connectionId] = new Place(playerId, worldId, roomKey);
        }
    }

    /// <summary>Renews every tracked connection's leases now.</summary>
    public async Task RenewAsync()
    {
        var connections = _connections.ToArray();
        var worlds = connections
            .GroupBy(c => c.Value.WorldId)
            .Select(world => grains.GetGrain<IWorldGrain>(world.Key).Renew(Sessions(world)));
        var rooms = connections
            .Where(c => c.Value.RoomKey is not null)
            .GroupBy(c => c.Value.RoomKey!)
            .Select(room => grains.GetGrain<IRoomGrain>(room.Key).Renew(Sessions(room)));
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

    private static List<PlayerSession> Sessions(IEnumerable<KeyValuePair<string, Place>> connections) =>
        [.. connections.Select(c => new PlayerSession(c.Value.PlayerId, c.Key))];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't renew this pod's session leases")]
    private static partial void LogRenewFailed(ILogger logger, Exception exception);

    private sealed record Place(Guid PlayerId, string WorldId, string? RoomKey);
}
