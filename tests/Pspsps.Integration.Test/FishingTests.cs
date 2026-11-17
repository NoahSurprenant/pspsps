using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Pspsps.Application.Players;
using Pspsps.Domain.Fishing;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Fishing at Town Square's pond, end to end through the hub, the room grain's bite timer and the database.</summary>
public sealed class FishingTests
{
    // Town Square of this world; each test uses its own guests, so other tests in the room don't matter.
    private const string WorldId = "tabby";

    // Town Square's pond is at (-7, -3), radius 2.5: this is at its edge.
    private const double PondEdgeX = -7;
    private const double PondEdgeZ = 0;

    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task ACatAtThePond_ReelingAtTheBite_CatchesAFish_AndIsPaid()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (_, felixHub) = await Game.ConnectGuestAsync();
        await using var tomConnection = tomHub;
        await using var felixConnection = felixHub;
        var bit = new TaskCompletionSource();
        tomHub.On("FishBit", () => bit.TrySetResult());
        var felixSees = new Recorder(felixHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await tomHub.InvokeAsync("Move", PondEdgeX, PondEdgeZ);

        await tomHub.InvokeAsync("Cast");
        await WaitForTheBite(bit);
        var reel = await tomHub.InvokeAsync<ReelView>("Reel");

        Assert.Multiple(() =>
        {
            Assert.That(reel.Outcome, Is.EqualTo("caught"));
            Assert.That(FishCatalog.All.Select(f => f.Id), Does.Contain(reel.FishId));
            Assert.That(reel.Coins, Is.EqualTo(FishCatalog.All.Single(f => f.Id == reel.FishId).Coins));
            Assert.That(reel.TotalCoins, Is.EqualTo(reel.Coins), "a new guest starts with no coins");
        });
        var saved = await Game.Get<IPlayerService>().FindAsync(tom.Id, CancellationToken.None);
        Assert.That(saved!.Coins, Is.EqualTo(reel.TotalCoins), "the coins are saved");
        var shown = await felixSees.Next<CatchView>("CatFished", c => c.PlayerId == tom.Id);
        Assert.That(shown, Is.EqualTo(new CatchView(tom.Id, reel.FishId!, reel.FishName!, reel.Coins)));
    }

    [Test]
    public async Task ReelingBeforeTheBite_LosesTheFish_AndPaysNothing()
    {
        var (tom, hub) = await Game.ConnectGuestAsync();
        await using var connection = hub;
        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await hub.InvokeAsync("Move", PondEdgeX, PondEdgeZ);

        await hub.InvokeAsync("Cast");
        var reel = await hub.InvokeAsync<ReelView>("Reel");

        Assert.That(reel, Is.EqualTo(new ReelView("too-early", null, null, 0, TotalCoins: null)));
        var saved = await Game.Get<IPlayerService>().FindAsync(tom.Id, CancellationToken.None);
        Assert.That(saved!.Coins, Is.Zero);
    }

    [Test]
    public async Task CastingAwayFromThePond_OrWithoutALine_IsRefusedWithAMessage()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var connection = hub;
        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);

        var far = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("Cast"));
        Assert.That(far!.Message, Does.Contain("Walk up to the pond to fish."));
        var noLine = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<ReelView>("Reel"));
        Assert.That(noLine!.Message, Does.Contain("Cast your line first."));
    }

    [Test]
    public async Task ACatch_IsSavedBeforeTheRoomSeesIt_AndEvenWhenTheTabClosesWhilePaying()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (_, felixHub) = await Game.ConnectGuestAsync();
        await using var tomConnection = tomHub;
        await using var felixConnection = felixHub;
        var bit = new TaskCompletionSource();
        tomHub.On("FishBit", () => bit.TrySetResult());
        var felixSees = new Recorder(felixHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await tomHub.InvokeAsync("Move", PondEdgeX, PondEdgeZ);
        await tomHub.InvokeAsync("Cast");
        await WaitForTheBite(bit);

        // Just before the catch is saved: has the room been shown it already? Then Tom's tab closes (a refresh, say).
        bool? shownBeforeSaving = null;
        Game.BeforeSaving.Once(tom.Id, async () =>
        {
            shownBeforeSaving = await felixSees.Saw<CatchView>("CatFished", c => c.PlayerId == tom.Id, TimeSpan.FromSeconds(1));
            await Task.WhenAny(tomHub.StopAsync(), Task.Delay(TimeSpan.FromSeconds(2)));
        });
        var reeling = tomHub.InvokeAsync<ReelView>("Reel");
        // Its answer may never come back: the tab has gone.
        await ((Task)reeling).ContinueWith(_ => { }, TaskScheduler.Default);

        var shown = await felixSees.Next<CatchView>("CatFished", c => c.PlayerId == tom.Id);
        var saved = await Game.Get<IPlayerService>().FindAsync(tom.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(shownBeforeSaving, Is.False, "the room saw the catch before it was paid");
            Assert.That(saved!.Coins, Is.EqualTo(shown.Coins), "the catch was paid although the tab closed");
        });
    }

    [Test]
    public async Task ANewTabTakingTheCatOver_ReelsTheOldTabsLineIn_AndNobodyGetsItsBite()
    {
        var (_, cookie) = await Game.SignInGuestAsync();
        await using var oldTab = await Game.ConnectAsync(cookie);
        await using var newTab = await Game.ConnectAsync(cookie);
        var oldBit = new TaskCompletionSource();
        var newBit = new TaskCompletionSource();
        oldTab.On("FishBit", () => oldBit.TrySetResult());
        newTab.On("FishBit", () => newBit.TrySetResult());
        await oldTab.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await oldTab.InvokeAsync("Move", PondEdgeX, PondEdgeZ);
        await oldTab.InvokeAsync("Cast");

        await newTab.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        Game.GrainClock.Advance(FishingLine.LongestWait + FishingLine.BiteWindow);

        await Task.WhenAny(oldBit.Task, newBit.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Multiple(() =>
        {
            Assert.That(oldBit.Task.IsCompleted, Is.False, "the replaced tab was told of a bite");
            Assert.That(newBit.Task.IsCompleted, Is.False, "the new tab was told of a bite for a line it never cast");
        });
        var noLine = Assert.ThrowsAsync<HubException>(() => newTab.InvokeAsync<ReelView>("Reel"));
        Assert.That(noLine!.Message, Does.Contain("Cast your line first."));
    }

    [Test]
    public async Task ABiteNotReeledInTime_GetsAway_AndTheTabIsTold()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var connection = hub;
        var bit = new TaskCompletionSource();
        var gotAway = new TaskCompletionSource();
        hub.On("FishBit", () => bit.TrySetResult());
        hub.On("FishGotAway", () => gotAway.TrySetResult());
        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", WorldId);
        await hub.InvokeAsync("Move", PondEdgeX, PondEdgeZ);
        await hub.InvokeAsync("Cast");
        await WaitForTheBite(bit);

        Game.GrainClock.Advance(FishingLine.BiteWindow + TimeSpan.FromSeconds(1));

        await gotAway.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var noLine = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<ReelView>("Reel"));
        Assert.That(noLine!.Message, Does.Contain("Cast your line first."));
    }

    // The bite comes at a random moment 3-9 s after the cast, on the grain clock: jump to the earliest it can come, then
    // move in half-second steps until the room says so, and reel at once. The clock stands still while waiting for the
    // timer's message, so the reel lands inside the bite window however long that takes.
    private static async Task WaitForTheBite(TaskCompletionSource bit)
    {
        var step = TimeSpan.FromMilliseconds(500);
        Game.GrainClock.Advance(FishingLine.ShortestWait - step);
        for (var waited = FishingLine.ShortestWait - step; waited <= FishingLine.LongestWait + step; waited += step)
        {
            Game.GrainClock.Advance(step);
            if (await Task.WhenAny(bit.Task, Task.Delay(TimeSpan.FromSeconds(1))) == bit.Task)
            {
                return;
            }
        }

        Assert.Fail("Nothing bit.");
    }
}
