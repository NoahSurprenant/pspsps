using Microsoft.AspNetCore.SignalR;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Hubs;

/// <summary>A connection's SignalR user is its player: <c>Clients.User(playerId)</c> reaches all their tabs, on any pod.</summary>
internal sealed class PlayerUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection?.User.GetPlayerId()?.ToString();

    /// <summary>The SignalR user of <paramref name="playerId"/>.</summary>
    public static string For(Guid playerId) => playerId.ToString();
}
