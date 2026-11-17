using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Shop;

/// <summary>
/// Everything the shop sells. Priced against fishing, which pays about 14 coins a catch on average (FishCatalog): a
/// collar or a party hat after two or three catches, a new coat after five or so, and a crown after an afternoon at the
/// pond. Furniture for a cat's home goes from a potted plant (three catches) to a sofa (a long sit at the pond). Every
/// price is more than zero.
/// </summary>
public static class ShopCatalog
{
    /// <summary>What a plain coat costs.</summary>
    public const int FurPrice = 75;

    /// <summary>What a two-coloured coat (calico, tuxedo) costs.</summary>
    public const int PatternedFurPrice = 150;

    /// <summary>How many of one piece of furniture a player can have: a home holds a few of each, not a warehouse.</summary>
    public const int MaxOfEachFurniture = 5;

    /// <summary>The furniture every cat is born with, as it is born with its coat: its own bed, for its home.</summary>
    public const string StarterFurniture = PropKinds.CatBed;

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
        // Furniture: each ID is the prop it's drawn as, each radius the size that prop is drawn at.
        new(PropKinds.Plant, "Potted plant", ItemKind.Furniture, 35, Radius: 0.5),
        new(PropKinds.FloorLamp, "Floor lamp", ItemKind.Furniture, 60, Radius: 0.4),
        new(PropKinds.CatBed, "Cat bed", ItemKind.Furniture, 70, Radius: 0.6),
        new(PropKinds.Rug, "Rug", ItemKind.Furniture, 90, Radius: 1.3),
        new(PropKinds.CafeTable, "Table and chairs", ItemKind.Furniture, 120, Radius: 1.2),
        new(PropKinds.Armchair, "Armchair", ItemKind.Furniture, 150, Radius: 0.7),
        new(PropKinds.Bookshelf, "Bookshelf", ItemKind.Furniture, 200, Radius: 0.9),
        new(PropKinds.CatTree, "Cat tree", ItemKind.Furniture, 300, Radius: 0.8),
        new(PropKinds.Sofa, "Sofa", ItemKind.Furniture, 350, Radius: 1.2),
    ];

    public static ShopItem? Find(string? id) => All.FirstOrDefault(item => item.Id == id);

    /// <summary>The shop's item for a fur colour: the coat a cat is born with, or a new one.</summary>
    public static ShopItem FurOf(FurColor fur) =>
        All.FirstOrDefault(item => item.Kind == ItemKind.Fur && item.Fur == fur)
        ?? throw new DomainException("That fur color doesn't exist.");
}
