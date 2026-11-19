using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Pspsps.Server.Moderation;

/// <summary>
/// The hub connections open on this pod, by player and by network (its keyed hash; none for an admin's): the ones to
/// close when a player, or a network, is banned.
/// </summary>
public sealed class PlayerConnections
{
    private readonly ConcurrentDictionary<string, Open> _open = new();

    public void Add(Guid playerId, HubCallerContext connection, string? addressHash = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _open[connection.ConnectionId] = new Open(playerId, connection, addressHash);
    }

    public void Remove(string connectionId) => _open.TryRemove(connectionId, out _);

    /// <summary>The player's connections open on this pod.</summary>
    public IReadOnlyList<HubCallerContext> Of(Guid playerId) =>
        [.. _open.Values.Where(open => open.PlayerId == playerId).Select(open => open.Connection)];

    /// <summary>The connections open on this pod from the network <paramref name="addressHash"/>, with their players.</summary>
    public IReadOnlyList<(Guid PlayerId, HubCallerContext Connection)> OnNetwork(string addressHash) =>
        [.. _open.Values.Where(open => open.AddressHash == addressHash).Select(open => (open.PlayerId, open.Connection))];

    private sealed record Open(Guid PlayerId, HubCallerContext Connection, string? AddressHash);
}
