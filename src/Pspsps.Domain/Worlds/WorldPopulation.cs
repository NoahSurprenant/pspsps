namespace Pspsps.Domain.Worlds;

/// <summary>
/// Who is in one world, against its population cap. Lives in memory only (the world's actor holds it). Counts players,
/// not tabs: a player with two tabs open counts once. Each tab's session holds a lease, like a cat in a room, so the
/// tabs of a pod that died stop counting once their leases run out.
/// </summary>
public sealed class WorldPopulation(WorldDefinition world)
{
    // Each player's sessions (browser tabs' connections) in this world, and when each last renewed its lease.
    private readonly Dictionary<Guid, Dictionary<string, DateTimeOffset>> _players = [];

    public WorldDefinition World { get; } = world;

    /// <summary>How many players are in the world.</summary>
    public int Count => _players.Count;

    /// <summary>
    /// Counts <paramref name="session"/> in, its lease starting <paramref name="now"/>; false when the world is full and
    /// the player isn't in it yet.
    /// </summary>
    public bool TryEnter(Guid playerId, string session, DateTimeOffset now)
    {
        if (!_players.TryGetValue(playerId, out var sessions))
        {
            if (_players.Count >= World.Capacity)
            {
                return false;
            }

            sessions = [];
            _players.Add(playerId, sessions);
        }

        sessions[session] = now;
        return true;
    }

    /// <summary>Counts <paramref name="session"/> out; the player leaves with their last one.</summary>
    public void Leave(Guid playerId, string session)
    {
        if (_players.TryGetValue(playerId, out var sessions) && sessions.Remove(session) && sessions.Count == 0)
        {
            _players.Remove(playerId);
        }
    }

    /// <summary>Renews <paramref name="session"/>'s lease; false when it isn't counted. A renewal never counts it back in.</summary>
    public bool Renew(Guid playerId, string session, DateTimeOffset now)
    {
        if (!_players.TryGetValue(playerId, out var sessions) || !sessions.ContainsKey(session))
        {
            return false;
        }

        sessions[session] = now;
        return true;
    }

    /// <summary>Counts out every session that stopped renewing its lease. Returns how many it counted out.</summary>
    public int RemoveExpired(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var (playerId, sessions) in _players.ToList())
        {
            foreach (var (session, renewedAt) in sessions.ToList())
            {
                if (SessionLease.HasExpired(renewedAt, now))
                {
                    sessions.Remove(session);
                    removed++;
                }
            }

            if (sessions.Count == 0)
            {
                _players.Remove(playerId);
            }
        }

        return removed;
    }
}
