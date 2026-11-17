using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

/// <summary>
/// A player as the game shows them: who they are, their coins, and what their cat wears (shop item IDs, as of
/// OutfitVersion).
/// </summary>
public sealed record PlayerProfile(
    Guid Id,
    string DisplayName,
    FurColor FurColor,
    int Coins,
    bool IsGuest,
    string? Hat = null,
    string? Collar = null,
    int OutfitVersion = 0)
{
    public static PlayerProfile From(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new(
            player.Id, player.DisplayName, player.FurColor, player.Coins, player.IsGuest, player.Hat, player.Collar,
            player.OutfitVersion);
    }
}
