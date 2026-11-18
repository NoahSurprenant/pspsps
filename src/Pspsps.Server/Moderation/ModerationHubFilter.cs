using Microsoft.AspNetCore.SignalR;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Moderation;

/// <summary>
/// A banned player gets nowhere: their connection is refused as it opens, and every call on one already open is refused
/// with why and until when (<see cref="StandingCache"/>). Also keeps <see cref="PlayerConnections"/>, the connections a
/// ban closes.
/// </summary>
public sealed class ModerationHubFilter(StandingCache standings, PlayerConnections connections) : IHubFilter
{
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
        await RefuseIfBannedAsync(context.Context);
        if (context.Context.User?.GetPlayerId() is { } playerId)
        {
            connections.Add(playerId, context.Context);
        }

        try
        {
            await next(context);
        }
        catch
        {
            connections.Remove(context.Context.ConnectionId);
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
        if (caller.User?.GetPlayerId() is { } playerId
            && await standings.BanOfAsync(playerId, caller.ConnectionAborted) is { } ban)
        {
            throw new HubException(ban.SaysBanned());
        }
    }
}
