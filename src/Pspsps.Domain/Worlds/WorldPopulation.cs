namespace Pspsps.Domain.Worlds;

/// <summary>
/// Who is in one world, against its population cap. Lives in memory only (the world's actor holds it). Counts players,
/// not tabs: a player with two tabs open counts once. Each tab's session holds a <see cref="SessionLease"/>, like a cat
/// in a room, so the tabs of a pod that died stop counting once their leases run out.
/// </summary>
public sealed class WorldPopulation(WorldDefinition world)
{
    // Each player's sessions (browser tabs' connections) in this world, by session.
    private readonly Dictionary<Guid, Dictionary<string, SessionLease>> _players = [];

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

        sessions[session] = sessions.TryGetValue(session, out var lease) ? lease.Renew(now) : SessionLease.Start(session, now);
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

    /// <summary>
    /// Renews <paramref name="session"/>'s lease. A session the world doesn't know (the world's actor came back empty
    /// after its pod died, or the lease ran out while the tab was still open) is counted back in, within the cap: the tab
    /// is live, since its pod renews it. False when it couldn't be, because the world is full.
    /// </summary>
    public bool Renew(Guid playerId, string session, DateTimeOffset now) => TryEnter(playerId, session, now);

    /// <summary>Counts out every session that stopped renewing its lease. Returns how many it counted out.</summary>
    public int RemoveExpired(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var (playerId, sessions) in _players.ToList())
        {
            foreach (var lease in sessions.Values.Where(l => l.HasExpired(now)).ToList())
            {
                sessions.Remove(lease.Session);
                removed++;
            }

            if (sessions.Count == 0)
            {
                _players.Remove(playerId);
            }
        }

        return removed;
    }
}
