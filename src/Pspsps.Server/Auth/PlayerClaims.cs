using System.Security.Claims;
using Pspsps.Application.Players;

namespace Pspsps.Server.Auth;

public static class PlayerClaims
{
    public const string PlayerIdType = "pspsps:player";

    public static ClaimsPrincipal For(PlayerProfile player) => new(new ClaimsIdentity(
        [new Claim(PlayerIdType, player.Id.ToString()), new Claim(ClaimTypes.Name, player.DisplayName)],
        AuthSetup.GameScheme));

    public static Guid? GetPlayerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PlayerIdType), out var id) ? id : null;
}
