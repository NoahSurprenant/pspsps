using Pspsps.Domain.Players;

namespace Pspsps.Application.Shop;

/// <summary>
/// A player's side of the shop: their coins, what they have (shop item IDs) and how many of each (furniture can be had
/// more than once), and what their cat wears (as of OutfitVersion).
/// </summary>
public sealed record Wardrobe(
    Guid PlayerId, int Coins, IReadOnlyDictionary<string, int> Counts, FurColor FurColor, string? Hat, string? Collar,
    int OutfitVersion = 0)
{
    /// <summary>The item IDs the player has.</summary>
    public IReadOnlyList<string> Owned => [.. Counts.Keys];

    public static Wardrobe Of(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new Wardrobe(
            player.Id,
            player.Coins,
            player.Items.ToDictionary(i => i.ItemId, i => i.Count),
            player.FurColor,
            player.Hat,
            player.Collar,
            player.OutfitVersion);
    }
}
