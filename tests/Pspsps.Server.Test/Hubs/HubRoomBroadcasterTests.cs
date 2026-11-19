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
            hub.Groups.RemoveFromGroupAsync("felix-tab", "tabby/cafe", Arg.Any<CancellationToken>());
            tab.SentOut(Guid.Empty, "Banned.");
        });
    }
}
