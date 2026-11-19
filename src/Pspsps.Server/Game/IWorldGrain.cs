using Orleans.CodeGeneration;

namespace Pspsps.Server.Game;

/// <summary>One world, keyed by its ID: who is in it, against its population cap.</summary>
/// <remarks>
/// Versioned: a change to it is add-only and bumps the version (docs/architecture.md, "Changing what pods say to each
/// other"), which Pspsps.Server.Test's WireContractTests check against wire-contracts.txt.
/// </remarks>
[Version(1)]
public interface IWorldGrain : IGrainWithStringKey
{
    /// <summary>
    /// Counts <paramref name="session"/> (a tab's connection) of the player in; false when the world is full and the
    /// player isn't in it yet.
    /// </summary>
    Task<bool> TryEnter(Guid playerId, string session);

    Task Leave(Guid playerId, string session);

    /// <summary>
    /// Renews these sessions' leases, counting back in (within the cap) any it forgot that didn't leave lately, as when
    /// this world came back empty after its pod died. Returns the sessions it couldn't count back in because it is full:
    /// those tabs have lost their place. The world counts out sessions whose lease runs out
    /// (<see cref="Pspsps.Domain.SessionLease"/>), as when the pod holding their connections died.
    /// </summary>
    Task<IReadOnlyList<string>> Renew(IReadOnlyList<PlayerSession> sessions);

    Task<int> GetPopulation();
}
