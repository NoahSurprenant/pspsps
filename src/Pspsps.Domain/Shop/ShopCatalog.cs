using Pspsps.Domain.Players;

namespace Pspsps.Domain.Shop;

/// <summary>
/// Everything the shop sells. Priced against fishing, which pays about 14 coins a catch on average (FishCatalog): a
/// collar or a party hat after two or three catches, a new coat after five or so, and a crown after an afternoon at the
/// pond. Every price is more than zero.
/// </summary>
public static class ShopCatalog
{
    /// <summary>What a plain coat costs.</summary>
    public const int FurPrice = 75;

    /// <summary>What a two-coloured coat (calico, tuxedo) costs.</summary>
    public const int PatternedFurPrice = 150;

    public static IReadOnlyList<ShopItem> All { get; } =
    [
        new("party-hat", "Party hat", ItemKind.Hat, 30),
        new("bow", "Bow", ItemKind.Hat, 45),
        new("beanie", "Beanie", ItemKind.Hat, 60),
        new("top-hat", "Top hat", ItemKind.Hat, 250),
        new("crown", "Crown", ItemKind.Hat, 1000),
        new("red-collar", "Red collar", ItemKind.Collar, 20),
        new("blue-collar", "Blue collar", ItemKind.Collar, 20),
        new("green-collar", "Green collar", ItemKind.Collar, 20),
        new("bell-collar", "Bell collar", ItemKind.Collar, 80),
        new("fur-orange", "Orange fur", ItemKind.Fur, FurPrice, FurColor.Orange),
        new("fur-black", "Black fur", ItemKind.Fur, FurPrice, FurColor.Black),
        new("fur-white", "White fur", ItemKind.Fur, FurPrice, FurColor.White),
        new("fur-grey", "Grey fur", ItemKind.Fur, FurPrice, FurColor.Grey),
        new("fur-cream", "Cream fur", ItemKind.Fur, FurPrice, FurColor.Cream),
        new("fur-brown", "Brown fur", ItemKind.Fur, FurPrice, FurColor.Brown),
        new("fur-calico", "Calico fur", ItemKind.Fur, PatternedFurPrice, FurColor.Calico),
        new("fur-tuxedo", "Tuxedo fur", ItemKind.Fur, PatternedFurPrice, FurColor.Tuxedo),
    ];

    public static ShopItem? Find(string? id) => All.FirstOrDefault(item => item.Id == id);

    /// <summary>The shop's item for a fur colour: the coat a cat is born with, or a new one.</summary>
    public static ShopItem FurOf(FurColor fur) =>
        All.FirstOrDefault(item => item.Kind == ItemKind.Fur && item.Fur == fur)
        ?? throw new DomainException("That fur color doesn't exist.");
}
