using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Server.Hubs;

namespace Pspsps.Server.Test.Hubs;

public sealed class HubGroupsTests
{
    [Test]
    public async Task RemoveAsync_WhenTheGroupChangeTakesLonger_GivesUpWithoutWaitingForIt()
    {
        var time = new FakeTimeProvider();
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        hub.Groups.RemoveFromGroupAsync("gone-tab", "tabby/cafe", Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);

        var removing = HubGroups.RemoveAsync(hub, "gone-tab", "tabby/cafe", time);
        Assert.That(removing.IsCompleted, Is.False);
        time.Advance(HubGroups.Wait);

        Assert.That(await removing, Is.False);
    }

    [Test]
    public async Task RemoveAsync_ObservesTheGroupChangeItGaveUpOn_SoItsLaterFaultIsntUnobserved()
    {
        // The backplane ack it stopped waiting for can still fault once it finally arrives (the connection's pod never
        // answered, say). Nothing else here ever awaits that task again: left alone, a faulted task whose exception
        // nobody reads raises TaskScheduler.UnobservedTaskException once it's collected.
        var time = new FakeTimeProvider();
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        var change = new TaskCompletionSource();
        hub.Groups.RemoveFromGroupAsync("gone-tab", "tabby/cafe", Arg.Any<CancellationToken>()).Returns(change.Task);

        var removing = HubGroups.RemoveAsync(hub, "gone-tab", "tabby/cafe", time);
        time.Advance(HubGroups.Wait);
        Assert.That(await removing, Is.False);

        UnobservedTaskExceptionEventArgs? unobserved = null;
        void OnUnobserved(object? _, UnobservedTaskExceptionEventArgs e)
        {
            unobserved = e;
            e.SetObserved();
        }
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            change.SetException(new TimeoutException("the backplane never answered"));
            // The CLR only raises UnobservedTaskException once a faulted, unobserved task is collected.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.That(unobserved, Is.Null, "the group change's fault must be observed, not left for the finalizer to report");
    }
}
