using Microsoft.AspNetCore.SignalR;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
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

    /// <summary>
    /// The networks <paramref name="banned"/> were banned: every tab on one (but an admin's) is told why and until when, as
    /// a banned player's are; every pod closes the connections it holds on them two seconds later, and their cats are
    /// taken out of their rooms. Never throws.
    /// </summary>
    Task NetworksBannedAsync(IReadOnlyList<BannedAddress> banned);

    /// <summary>The networks' bans were lifted: this pod lets them in at once (other pods within seconds).</summary>
    void NetworksLifted(IReadOnlyList<string> addressHashes);
}

internal sealed partial class ModerationEffects(
    IHubContext<GameHub, IGameClient> hub,
    IModerationSignal signal,
    PlayerConnections connections,
    StandingCache standings,
    IGrainFactory grains,
    ILogger<ModerationEffects> logger,
    AddressBanCache? networks = null,
    IRoomLibrary? rooms = null) : IModerationEffects
{
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

    public async Task NetworksBannedAsync(IReadOnlyList<BannedAddress> banned)
    {
        ArgumentNullException.ThrowIfNull(banned);
        foreach (var network in banned)
        {
            networks?.Know(network.AddressHash, network.Ban);
            try
            {
                // To the SignalR group of the network, which the backplane carries to every pod's tabs on it.
                await hub.Clients.Group(AddressGuard.GroupOf(network.AddressHash)).Banned(SanctionView.From(network.Ban));
                // Every pod closes the connections it holds on the network two seconds later (ModerationOnThisPod,
                // through the same cross-pod signal as a player's ban), and reads the ban again rather than trusting
                // what it cached.
                await signal.SendNetworkAsync(network.AddressHash, close: true);
                // Only the players this pod itself holds a connection for: a tab on another pod is sent out of its room
                // when that pod notices the ban (ModerationHubFilter, once the signal or AddressBanCache.KeepFor catches up).
                foreach (var playerId in connections.OnNetwork(network.AddressHash).Select(open => open.PlayerId).Distinct())
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

    // Out of whatever room the cat is in, wherever its tab's connection is: a tab on another pod that ignores the ban
    // doesn't leave a cat standing there.
    private async Task SendOutOfRoomAsync(Guid playerId, Sanction ban)
    {
        if (await grains.GetGrain<IPlayerSessionGrain>(playerId).Locate() is { } place)
        {
            await grains.GetGrain<IRoomGrain>(RoomKey.Grain(place.WorldId, place.RoomId, rooms?.Find(place.RoomId)?.Place is not null)).SendOut([playerId], ban.SaysBanned());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell the tabs on network {Network} of its ban; they are refused at their next call")]
    private static partial void LogNetworkNotTold(ILogger logger, Exception exception, string network);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell player {PlayerId} of the moderation ({Action}); it shows when they next connect or speak")]
    private static partial void LogNotTold(ILogger logger, Exception exception, Guid playerId, ModerationAction action);
}
