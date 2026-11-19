using Microsoft.AspNetCore.SignalR;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Moderation;

/// <summary>
/// A banned player gets nowhere: their connection is refused as it opens, and every call on one already open is refused
/// with why and until when (<see cref="StandingCache"/>). So does a player on a banned network (<see cref="AddressGuard"/>;
/// never an admin): the network is the one the connection opened from, remembered for the player (7 days) as it opens.
/// Also keeps <see cref="PlayerConnections"/>, the connections a ban closes, and puts each connection (but an admin's) in
/// its network's group, which a network's ban is told to.
/// </summary>
public sealed class ModerationHubFilter(
    StandingCache standings, PlayerConnections connections, AddressGuard? networks = null) : IHubFilter
{
    // Where a connection keeps the network it opened from (its hash; none for an admin's, or with network bans off).
    private const string NetworkItem = "pspsps:network";

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            await RefuseIfBannedAsync(invocationContext.Context);
        }
        catch (HubException)
        {
            // Closed too, on whichever pod it is: a tab that ignored the ban (or was told on another pod) gets nothing more.
            invocationContext.Context.Abort();
            throw;
        }

        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var caller = context.Context;
        var playerId = caller.User?.GetPlayerId();
        var network = networks is not null && playerId is not null && caller.GetHttpContext() is { } http
            ? networks.NetworkOf(http, playerId)
            : null;
        if (network is not null)
        {
            caller.Items[NetworkItem] = network;
        }

        await RefuseIfBannedAsync(caller);
        if (playerId is { } id)
        {
            connections.Add(id, caller, network);
        }

        try
        {
            if (network is not null && playerId is { } seen)
            {
                await networks!.SeenAsync(network, seen);
                await context.Hub.Groups.AddToGroupAsync(caller.ConnectionId, AddressGuard.GroupOf(network));
            }

            await next(context);
        }
        catch
        {
            connections.Remove(caller.ConnectionId);
            throw;
        }
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        connections.Remove(context.Context.ConnectionId);
        await next(context, exception);
    }

    private async Task RefuseIfBannedAsync(HubCallerContext caller)
    {
        if (caller.User?.GetPlayerId() is not { } playerId)
        {
            return;
        }

        if (await standings.BanOfAsync(playerId, caller.ConnectionAborted) is { } ban)
        {
            throw new HubException(ban.SaysBanned());
        }

        if (networks is not null && caller.Items.TryGetValue(NetworkItem, out var held) && held is string network
            && await networks.BanOfAsync(network, caller.ConnectionAborted) is { } networkBan)
        {
            throw new HubException(networkBan.SaysBanned());
        }
    }
}
