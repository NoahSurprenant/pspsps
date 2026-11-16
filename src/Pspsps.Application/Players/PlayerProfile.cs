using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

public sealed record PlayerProfile(Guid Id, string DisplayName, FurColor FurColor, int Coins, bool IsGuest)
{
    public static PlayerProfile From(Player player) =>
        new(player.Id, player.DisplayName, player.FurColor, player.Coins, player.IsGuest);
}
