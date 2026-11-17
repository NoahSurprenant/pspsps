using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Domain;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;

namespace Pspsps.Integration.Test;

/// <summary>
/// A pod that dies without closing its connections: their cats leave their rooms, and their tabs stop counting in their
/// worlds, once their leases run out. A live tab's cat stays, renewed by its pod. The grains' clock is moved by hand.
/// </summary>
public sealed class SessionLeaseTests
{
    private const string WorldId = "calico";
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task ACatWhosePodDied_LeavesWhenItsLeaseRunsOut_AndALiveTabsCatStays()
    {
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = felixHub;
        var felixSees = new Recorder(felixHub);
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);

        // Tom's tab was on a pod that died: his world and room still count him, and nothing renews his leases.
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        var world = grains.GetGrain<IWorldGrain>(WorldId);
        var room = grains.GetGrain<IRoomGrain>(RoomKey.For(WorldId, "town-square"));
        var tom = new PlayerCard(Guid.NewGuid(), "Tom", "Grey");
        const string deadTab = "a-dead-pods-connection";
        Assert.That(await world.TryEnter(tom.PlayerId, deadTab), Is.True);
        var joined = await room.Join(tom, deadTab);
        Assert.That(joined.Cats.Select(c => c.PlayerId), Is.EquivalentTo(new[] { felix.Id, tom.PlayerId }));
        var population = await world.GetPopulation();

        // Most of a lease later, Felix's pod renews his; then Tom's runs out, and Felix's is still young.
        Game.GrainClock.Advance(SessionLease.Duration - SessionLease.RenewEvery);
        await Game.Get<SessionHeartbeat>().RenewAsync();
        Game.GrainClock.Advance(SessionLease.RenewEvery + TimeSpan.FromSeconds(1));

        Assert.That(await felixSees.Next<Guid>("CatLeft", id => id == tom.PlayerId), Is.EqualTo(tom.PlayerId));
        // Less, not exactly one less: a tab an earlier test closed may have been on its way out too.
        Assert.That(await world.GetPopulation(), Is.LessThan(population), "Tom's tab no longer counts in the world");

        // Felix is still in the room and the world.
        await felixHub.InvokeAsync("Move", 1.0, 1.0);
        Assert.That(await felixSees.Next<(Guid, double, double)>("CatMoved", m => m.Item1 == felix.Id),
            Is.EqualTo((felix.Id, 1.0, 1.0)));
        var worlds = await felixHub.InvokeAsync<List<WorldSummary>>("GetWorlds");
        Assert.That(worlds.Single(w => w.Id == WorldId).Population, Is.GreaterThanOrEqualTo(1), "Felix still counts");
    }
}
