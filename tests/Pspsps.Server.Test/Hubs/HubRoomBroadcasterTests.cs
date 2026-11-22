using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;

namespace Pspsps.Server.Test.Hubs;

public sealed class HubRoomBroadcasterTests
{
    [Test]
    public async Task SendingATabOut_WhoseConnectionNoPodAnswersFor_DoesntHoldUpTheRoom()
    {
        // The Redis backplane's group change for a connection no pod has: no answer for 30 seconds.
        var time = new FakeTimeProvider();
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        hub.Groups.RemoveFromGroupAsync("gone-tab", "tabby/cafe", Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);
        var tab = Substitute.For<IGameClient>();
        hub.Clients.Client("gone-tab").Returns(tab);

        var sending = new HubRoomBroadcaster(hub, time).SentOut("tabby/cafe", "gone-tab", "Out you go.");
        Assert.That(sending.IsCompleted, Is.False);
        time.Advance(HubGroups.Wait);
        await sending;

        await tab.Received(1).SentOut(Guid.Empty, "Out you go.");
    }

    [Test]
    public async Task SendingATabOut_TakesItOutOfTheRoomsGroupFirst()
    {
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        var tab = Substitute.For<IGameClient>();
        hub.Clients.Client("felix-tab").Returns(tab);

        await new HubRoomBroadcaster(hub, new FakeTimeProvider()).SentOut("tabby/cafe", "felix-tab", "Banned.");

        Received.InOrder(() =>
        {
            _ = hub.Groups.RemoveFromGroupAsync("felix-tab", "tabby/cafe", Arg.Any<CancellationToken>());
            _ = tab.SentOut(Guid.Empty, "Banned.");
        });
    }

    [Test]
    public async Task ARoomOnAStoppingPod_TellsEachOfItsTabsItIsLost_EvenWhenOneCantBeTold()
    {
        var broadcaster = Substitute.For<IRoomBroadcaster>();
        broadcaster.RoomLost("gone-tab").Returns(Task.FromException(new IOException("gone")));

        await RoomGrain.TellLost(["gone-tab", "felix-tab"], broadcaster, CancellationToken.None);

        await broadcaster.Received(1).RoomLost("felix-tab");
    }

    [Test]
    public async Task TellingALostRoomsTabs_DoesntOutlastTheDeactivation_WhenTheBackplaneHangs()
    {
        // Told all at once, and given up when the deactivation is: a slow backplane doesn't hold up the pod's shutdown.
        var broadcaster = Substitute.For<IRoomBroadcaster>();
        broadcaster.RoomLost(Arg.Any<string>()).Returns(new TaskCompletionSource().Task);
        using var deactivation = new CancellationTokenSource();

        var telling = RoomGrain.TellLost(["tom-tab", "felix-tab"], broadcaster, deactivation.Token);
        await broadcaster.Received(1).RoomLost("tom-tab");
        await broadcaster.Received(1).RoomLost("felix-tab");
        await deactivation.CancelAsync();

        Assert.That(async () => await telling.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
    }

    [Test]
    public async Task ALostRoom_IsToldToTheTabsConnection()
    {
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        var tab = Substitute.For<IGameClient>();
        hub.Clients.Client("felix-tab").Returns(tab);

        await new HubRoomBroadcaster(hub, new FakeTimeProvider()).RoomLost("felix-tab");

        await tab.Received(1).RoomLost();
    }
}
