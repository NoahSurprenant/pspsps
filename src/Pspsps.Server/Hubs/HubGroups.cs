using Microsoft.AspNetCore.SignalR;

namespace Pspsps.Server.Hubs;

/// <summary>Taking a connection out of a group from outside its hub call: from a grain, or the heartbeat.</summary>
internal static class HubGroups
{
    /// <summary>
    /// How long a group change is waited for. For a connection on another pod, SignalR's Redis backplane waits until that
    /// pod says it's done, and up to 30 seconds when no pod answers (the connection closed meanwhile, or its pod died):
    /// a grain waiting that long would hold up its whole room.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Takes <paramref name="connectionId"/> out of <paramref name="group"/>, waiting at most <see cref="Wait"/>; false
    /// when it took longer (the change may still happen, and a closed connection is in no group anyway).
    /// </summary>
    public static async Task<bool> RemoveAsync(
        IHubContext<GameHub, IGameClient> hub, string connectionId, string group, TimeProvider time)
    {
        var removing = hub.Groups.RemoveFromGroupAsync(connectionId, group);
        try
        {
            await removing.WaitAsync(Wait, time);
            return true;
        }
        catch (TimeoutException)
        {
            // WaitAsync gives up on removing, not removing itself: it keeps running (SignalR's backplane ack can take
            // up to 30 seconds), and nothing else here ever awaits it. Observed so a later fault doesn't surface as an
            // unobserved task exception; harmless either way, as above.
            Observe(removing);
            return false;
        }
    }

    private static void Observe(Task task) => task.ContinueWith(
        static t => _ = t.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
