using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Pspsps.Server.Moderation;

/// <summary>The hub connections open on this pod, by player: the ones to close when a player is banned.</summary>
public sealed class PlayerConnections
{
    private readonly ConcurrentDictionary<string, Open> _open = new();

    public void Add(Guid playerId, HubCallerContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _open[connection.ConnectionId] = new Open(playerId, connection);
    }

    public void Remove(string connectionId) => _open.TryRemove(connectionId, out _);

    /// <summary>The player's connections open on this pod.</summary>
    public IReadOnlyList<HubCallerContext> Of(Guid playerId) =>
        [.. _open.Values.Where(open => open.PlayerId == playerId).Select(open => open.Connection)];

    private sealed record Open(Guid PlayerId, HubCallerContext Connection);
}
