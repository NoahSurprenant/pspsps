using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;

namespace Pspsps.Server.Moderation;

/// <summary>What a moderation does once it's saved: the player is told, and a banned one is put out of the game.</summary>
public interface IModerationEffects
{
    /// <summary>
    /// <paramref name="action"/> was done to <paramref name="playerId"/>, who stands at <paramref name="standing"/> now:
    /// their tabs are told (a warning to fetch, their mute, their ban). A banned player's tabs are told why and until when,
    /// then closed, and their cat is taken out of its room. Never throws: the change is saved, and shows anyway the next
    /// time they connect or speak.
    /// </summary>
    Task AppliedAsync(Guid playerId, ModerationAction action, Standing standing);
}

internal sealed partial class ModerationEffects(
    IHubContext<GameHub, IGameClient> hub,
    PlayerConnections connections,
    StandingCache standings,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<ModerationEffects> logger) : IModerationEffects
{
    /// <summary>How long a banned player's tab has to hear why before its connection is closed.</summary>
    public static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(2);

    // The timers that will close banned players' connections: kept, so they aren't collected before they fire.
    private readonly ConcurrentDictionary<ITimer, byte> _closing = new();

    public async Task AppliedAsync(Guid playerId, ModerationAction action, Standing standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        standings.Forget(playerId);
        var player = hub.Clients.User(PlayerUserIdProvider.For(playerId));
        try
        {
            switch (action)
            {
                case ModerationAction.Warn:
                    await player.Warned();
                    break;
                case ModerationAction.Mute:
                    await player.Muted(SanctionView.From(standing.Mute));
                    break;
                case ModerationAction.Lift:
                    // A ban lifted has no tab to tell: the player's next visit just works.
                    await player.Muted(null);
                    break;
                case ModerationAction.Ban when standing.Ban is { } ban:
                    // This pod's tabs are closed shortly; another pod's are told too, and refused from then on.
                    CloseLater(playerId);
                    await player.Banned(SanctionView.From(ban)!);
                    await SendOutOfRoomAsync(playerId, ban);
                    break;
                default:
                    break;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogNotTold(logger, e, playerId, action);
        }
    }

    // Closes the player's connections on this pod after CloseAfter, once their tabs have had time to hear why.
    private void CloseLater(Guid playerId)
    {
        var open = connections.Of(playerId);
        if (open.Count == 0)
        {
            return;
        }

        ITimer? timer = null;
        timer = time.CreateTimer(state =>
        {
            foreach (var connection in open)
            {
                connection.Abort();
            }

            if (timer is not null && _closing.TryRemove(timer, out _))
            {
                timer.Dispose();
            }
        }, null, CloseAfter, Timeout.InfiniteTimeSpan);
        _closing[timer] = 0;
    }

    // Out of whatever room the cat is in, wherever its tab's connection is: a tab on another pod that ignores the ban
    // doesn't leave a cat standing there.
    private async Task SendOutOfRoomAsync(Guid playerId, Sanction ban)
    {
        if (await grains.GetGrain<IPlayerSessionGrain>(playerId).Locate() is { } place)
        {
            await grains.GetGrain<IRoomGrain>(RoomKey.For(place.WorldId, place.RoomId)).SendOut([playerId], ban.SaysBanned());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId} of the moderation ({Action}); it shows when they next connect or speak")]
    private static partial void LogNotTold(ILogger logger, Exception exception, Guid playerId, ModerationAction action);
}
