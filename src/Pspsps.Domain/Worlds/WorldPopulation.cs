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

    // Sessions that left lately, and when: a renewal sent before a tab left can arrive after it, and mustn't count it back
    // in. Kept for one lease; no renewal is that late.
    private readonly Dictionary<string, DateTimeOffset> _left = [];

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

        _left.Remove(session);
        sessions[session] = sessions.TryGetValue(session, out var lease) ? lease.Renew(now) : SessionLease.Start(session, now);
        return true;
    }

    /// <summary>Counts <paramref name="session"/> out; the player leaves with their last one.</summary>
    public void Leave(Guid playerId, string session, DateTimeOffset now)
    {
        _left[session] = now;
        if (_players.TryGetValue(playerId, out var sessions) && sessions.Remove(session) && sessions.Count == 0)
        {
            _players.Remove(playerId);
        }
    }

    /// <summary>
    /// Renews <paramref name="session"/>'s lease. A session the world doesn't know and that didn't leave it lately is a
    /// live tab the world forgot (it came back empty after its pod died, or the lease ran out while the tab was still
    /// open: its pod renews it, so it's live), and is counted back in within the cap.
    /// </summary>
    public TabRenewal Renew(Guid playerId, string session, DateTimeOffset now)
    {
        if (_players.TryGetValue(playerId, out var sessions) && sessions.TryGetValue(session, out var lease))
        {
            sessions[session] = lease.Renew(now);
            return TabRenewal.Renewed;
        }

        if (_left.ContainsKey(session))
        {
            return TabRenewal.Left;
        }

        return TryEnter(playerId, session, now) ? TabRenewal.CountedBackIn : TabRenewal.Full;
    }

    /// <summary>Counts out every session that stopped renewing its lease. Returns how many it counted out.</summary>
    public int RemoveExpired(DateTimeOffset now)
    {
        foreach (var session in _left.Where(l => now - l.Value > SessionLease.Duration).Select(l => l.Key).ToList())
        {
            _left.Remove(session);
        }

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
