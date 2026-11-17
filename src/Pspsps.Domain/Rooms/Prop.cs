namespace Pspsps.Domain.Rooms;

/// <summary>
/// A piece of a room's decoration: what it is (<see cref="Kind"/>, one of <see cref="PropKinds"/>, which the browser
/// draws), where it stands (<see cref="Spot"/>), the circle round that spot it is drawn inside (<see cref="Radius"/>),
/// and which way it faces (<see cref="Facing"/>, in radians about the vertical: 0 faces the front of the room, where
/// the camera is). Decoration only: cats walk through props.
/// </summary>
public sealed record Prop(string Kind, Position Spot, double Radius, double Facing = 0);

/// <summary>The kinds of prop the browser knows how to draw: the rooms' own, and the furniture in players' homes.</summary>
public static class PropKinds
{
    public const string Tree = "tree";
    public const string Bench = "bench";
    public const string Lamp = "lamp";
    public const string FlowerBed = "flower-bed";
    public const string CafeTable = "cafe-table";
    public const string Counter = "counter";
    public const string Plant = "plant";
    public const string CatTree = "cat-tree";
    public const string Rug = "rug";
    public const string Armchair = "armchair";
    public const string Sofa = "sofa";
    public const string FloorLamp = "floor-lamp";
    public const string Bookshelf = "bookshelf";
    public const string CatBed = "cat-bed";

    /// <summary>The shop's catalogue, open on a lectern: more than decoration, it is where things are bought.</summary>
    public const string CatalogueStand = "catalogue-stand";
    public const string HatShelf = "hat-shelf";
    public const string ShopCounter = "shop-counter";
    public const string Mirror = "mirror";

    public static IReadOnlyList<string> All { get; } =
    [
        Tree, Bench, Lamp, FlowerBed, CafeTable, Counter, Plant, CatTree, Rug, Armchair, Sofa, FloorLamp, Bookshelf, CatBed,
        CatalogueStand, HatShelf, ShopCounter, Mirror,
    ];
}
