using Orleans.Runtime;
using Pspsps.Domain;
using Pspsps.Domain.Worlds;

namespace Pspsps.Server.Game;

/// <summary>
/// Holds a world's <see cref="WorldPopulation"/> in memory and counts out sessions whose lease ran out. Kept alive: its
/// counts exist only in memory.
/// </summary>
[KeepAlive]
internal sealed class WorldGrain([FromKeyedServices(TimeProviderNames.Grains)] TimeProvider time) : Grain, IWorldGrain
{
    private WorldPopulation _population = null!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var id = this.GetPrimaryKeyString();
        var world = WorldCatalog.Find(id) ?? throw new InvalidOperationException($"No world '{id}'.");
        _population = new WorldPopulation(world);
        this.RegisterGrainTimer(
            static (grain, _) => grain.RemoveExpired(),
            this,
            new GrainTimerCreationOptions { DueTime = SessionLease.RenewEvery, Period = SessionLease.RenewEvery });
        return Task.CompletedTask;
    }

    public Task<bool> TryEnter(Guid playerId, string session) =>
        Task.FromResult(_population.TryEnter(playerId, session, time.GetUtcNow()));

    public Task Leave(Guid playerId, string session)
    {
        _population.Leave(playerId, session);
        return Task.CompletedTask;
    }

    public Task Renew(IReadOnlyList<PlayerSession> sessions)
    {
        var now = time.GetUtcNow();
        foreach (var session in sessions)
        {
            _population.Renew(session.PlayerId, session.Session, now);
        }

        return Task.CompletedTask;
    }

    public Task<int> GetPopulation() => Task.FromResult(_population.Count);

    private Task RemoveExpired()
    {
        _population.RemoveExpired(time.GetUtcNow());
        return Task.CompletedTask;
    }
}
