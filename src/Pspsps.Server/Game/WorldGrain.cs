using Orleans.Runtime;
using Pspsps.Domain;
using Pspsps.Domain.Worlds;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Game;

/// <summary>
/// Holds a world's <see cref="WorldPopulation"/> in memory and counts out sessions whose lease ran out; renewals count
/// live ones back in. Kept alive: its counts exist only in memory.
/// </summary>
[KeepAlive]
internal sealed class WorldGrain(
    [FromKeyedServices(TimeProviderNames.Grains)] TimeProvider time,
    GameMetrics metrics) : Grain, IWorldGrain
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
        _population.Leave(playerId, session, time.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> Renew(IReadOnlyList<PlayerSession> sessions)
    {
        var now = time.GetUtcNow();
        IReadOnlyList<string> refused = [.. sessions
            .Where(s => _population.Renew(s.PlayerId, s.Session, now) == TabRenewal.Full)
            .Select(s => s.Session)];
        return Task.FromResult(refused);
    }

    public Task<int> GetPopulation() => Task.FromResult(_population.Count);

    private Task RemoveExpired()
    {
        var expired = _population.RemoveExpired(time.GetUtcNow());
        if (expired > 0)
        {
            metrics.LeasesExpired("world", expired);
        }

        return Task.CompletedTask;
    }
}
