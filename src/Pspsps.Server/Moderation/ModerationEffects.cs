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

    /// <summary>
    /// The networks <paramref name="banned"/> were banned: every tab on one (but an admin's) is told why and until when, as
    /// a banned player's are; this pod's are closed shortly, and their cats taken out of their rooms. Never throws.
    /// </summary>
    Task NetworksBannedAsync(IReadOnlyList<BannedAddress> banned);

    /// <summary>The networks' bans were lifted: this pod lets them in at once (other pods within seconds).</summary>
    void NetworksLifted(IReadOnlyList<string> addressHashes);
}

internal sealed partial class ModerationEffects(
    IHubContext<GameHub, IGameClient> hub,
    PlayerConnections connections,
    StandingCache standings,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<ModerationEffects> logger,
    AddressBanCache? networks = null) : IModerationEffects
{
    /// <summary>How long a banned player's tab has to hear why before its connection is closed.</summary>
    public static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(2);

    // The timers that will close banned players' connections: kept, so they aren't collected before they fire.
    private readonly ConcurrentDictionary<ITimer, byte> _closing = new();

    public async Task AppliedAsync(Guid playerId, ModerationAction action, Standing standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        standings.Know(playerId, standing);
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

    public async Task NetworksBannedAsync(IReadOnlyList<BannedAddress> banned)
    {
        ArgumentNullException.ThrowIfNull(banned);
        foreach (var network in banned)
        {
            networks?.Know(network.AddressHash, network.Ban);
            var here = connections.OnNetwork(network.AddressHash);
            try
            {
                // This pod's tabs on it are closed shortly; another pod's are told too, and refused from then on.
                CloseLater([.. here.Select(open => open.Connection)]);
                await hub.Clients.Group(AddressGuard.GroupOf(network.AddressHash)).Banned(SanctionView.From(network.Ban));
                foreach (var playerId in here.Select(open => open.PlayerId).Distinct())
                {
                    await SendOutOfRoomAsync(playerId, network.Ban);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The network's label only (the first digits of its hash), never the address.
                LogNetworkNotTold(logger, e, network.Label);
            }
        }
    }

    public void NetworksLifted(IReadOnlyList<string> addressHashes)
    {
        ArgumentNullException.ThrowIfNull(addressHashes);
        foreach (var hash in addressHashes)
        {
            networks?.Know(hash, null);
        }
    }

    // Closes the player's connections on this pod after CloseAfter, once their tabs have had time to hear why.
    private void CloseLater(Guid playerId) => CloseLater(connections.Of(playerId));

    private void CloseLater(IReadOnlyList<HubCallerContext> open)
    {
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell the tabs on network {Network} of its ban; they are refused at their next call")]
    private static partial void LogNetworkNotTold(ILogger logger, Exception exception, string network);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId} of the moderation ({Action}); it shows when they next connect or speak")]
    private static partial void LogNotTold(ILogger logger, Exception exception, Guid playerId, ModerationAction action);
}
