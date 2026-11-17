namespace Pspsps.Server.Game;

/// <summary>One world, keyed by its ID: who is in it, against its population cap.</summary>
public interface IWorldGrain : IGrainWithStringKey
{
    /// <summary>
    /// Counts <paramref name="session"/> (a tab's connection) of the player in; false when the world is full and the
    /// player isn't in it yet.
    /// </summary>
    Task<bool> TryEnter(Guid playerId, string session);

    Task Leave(Guid playerId, string session);

    /// <summary>
    /// Renews these sessions' leases, counting back in (within the cap) any it forgot, as when this world came back
    /// empty after its pod died. The world counts out sessions whose lease runs out
    /// (<see cref="Pspsps.Domain.SessionLease"/>), as when the pod holding their connections died.
    /// </summary>
    Task Renew(IReadOnlyList<PlayerSession> sessions);

    Task<int> GetPopulation();
}
