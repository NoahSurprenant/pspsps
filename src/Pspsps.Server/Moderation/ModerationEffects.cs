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
    /// then closed on every pod, and their cat is taken out of its room. Never throws: the change is saved, and shows
    /// anyway the next time they connect or speak.
    /// </summary>
    Task AppliedAsync(Guid playerId, ModerationAction action, Standing standing);
}

internal sealed partial class ModerationEffects(
    IHubContext<GameHub, IGameClient> hub,
    IModerationSignal signal,
    StandingCache standings,
    IGrainFactory grains,
    ILogger<ModerationEffects> logger) : IModerationEffects
{
    /// <summary>How long a banned player's tab has to hear why before its connection is closed.</summary>
    public static TimeSpan CloseAfter => ModerationOnThisPod.CloseAfter;

    public async Task AppliedAsync(Guid playerId, ModerationAction action, Standing standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        standings.Know(playerId, standing);
        // Through SignalR's user, which the backplane carries to the player's tabs on every pod.
        var player = hub.Clients.User(PlayerUserIdProvider.For(playerId));
        try
        {
            // Every pod reads the new standing at the player's next call, and a ban closes their tabs wherever they are.
            var ban = action == ModerationAction.Ban ? standing.Ban : null;
            await signal.SendAsync(playerId, close: ban is not null);
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
                case ModerationAction.Ban when ban is not null:
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
