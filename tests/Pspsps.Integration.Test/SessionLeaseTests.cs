using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Domain;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Telemetry;

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

    [Test]
    public async Task ATabWhoseCatTheRoomLost_IsToldAndWalksBackIn()
    {
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = felixHub;
        var lost = new TaskCompletionSource();
        felixHub.On("RoomLost", () => lost.TrySetResult());
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");

        // The café forgot Felix's cat (its pod died and it came back empty, say) while his tab stayed open.
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        await grains.GetGrain<IRoomGrain>(RoomKey.For(WorldId, "cafe")).Leave(felix.Id, felixHub.ConnectionId!);
        await Game.Get<SessionHeartbeat>().RenewAsync();

        await lost.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // What the browser does then: back into the world, then the room it was in.
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        var room = await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");
        Assert.That(room.Cats.Select(c => c.PlayerId), Does.Contain(felix.Id));
    }

    [Test]
    public async Task ATabWhoseRoomLostItsCat_HearsNothingMoreFromThatRoom()
    {
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        await using var _ = felixHub;
        await using var __ = tomHub;
        var lost = new TaskCompletionSource();
        felixHub.On("RoomLost", () => lost.TrySetResult());
        var felixSees = new Recorder(felixHub);
        foreach (var hub in new[] { felixHub, tomHub })
        {
            await hub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
            await hub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");
        }

        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        await grains.GetGrain<IRoomGrain>(RoomKey.For("siamese", "cafe")).Leave(felix.Id, felixHub.ConnectionId!);
        await Game.Get<SessionHeartbeat>().RenewAsync();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await tomHub.InvokeAsync("Move", 2.0, 2.0);
        await tomHub.InvokeAsync("Say", "anyone?");

        Assert.That(await felixSees.Saw<ChatView>("ChatSaid", c => c.PlayerId == tom.Id, TimeSpan.FromSeconds(2)), Is.False);
    }

    [Test]
    public async Task ARenewalThatWakesAnEmptyRoom_DoesNotCountItActive()
    {
        var activeRooms = new List<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument is { Name: "pspsps.rooms.active", Meter.Name: GameMetrics.MeterName })
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (activeRooms)
            {
                activeRooms.Add(value);
            }
        });
        listener.Start();

        // A room grain nobody has activated (the room's actor only checks the room part of its key).
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        var lost = await grains.GetGrain<IRoomGrain>(RoomKey.For($"world-{Guid.NewGuid():N}", "cafe"))
            .Renew([new PlayerSession(Guid.NewGuid(), "a-tab-on-a-live-pod")]);

        Assert.That(lost, Is.EqualTo(new[] { "a-tab-on-a-live-pod" }));
        lock (activeRooms)
        {
            Assert.That(activeRooms, Does.Not.Contain(1L), "an empty room isn't an active one");
        }
    }
}
