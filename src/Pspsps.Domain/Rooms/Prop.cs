namespace Pspsps.Domain.Rooms;

/// <summary>
/// A piece of a room's decoration: what it is (<see cref="Kind"/>, one of <see cref="PropKinds"/>, which the browser
/// draws), where it stands (<see cref="Spot"/>), the circle round that spot it is drawn inside (<see cref="Radius"/>),
/// and which way it faces (<see cref="Facing"/>, in radians about the vertical: 0 faces the front of the room, where
/// the camera is). Decoration only: cats walk through props.
/// </summary>
public sealed record Prop(string Kind, Position Spot, double Radius, double Facing = 0)
{
    public static Prop BenchFacing(Position seat, Position view) =>
        new(PropKinds.Bench, seat, 1, Math.Atan2(seat.X - view.X, seat.Z - view.Z));
}

/// <summary>The kinds of prop the browser knows how to draw: the rooms' own, and the furniture in players' homes.</summary>
public static class PropKinds
{
    public const string MapBoard = "map-board";
    public const string Piano = "piano";
    public const string PottingBench = "potting-bench";
    public const string RodRack = "rod-rack";
    public const string PearTree = "pear-tree";
    public const string PackingCrate = "packing-crate";
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
    public const string MarketStall = "market-stall";
    public const string PizzaTable = "pizza-table";
    public const string PizzaOven = "pizza-oven";
    public const string Stage = "stage";
    public const string SpeakerStack = "speaker-stack";
    public const string LightRig = "light-rig";
    public const string FishBarrel = "fish-barrel";
    public const string YarnBasket = "yarn-basket";
    public const string MarketCart = "market-cart";
    public const string Chalkboard = "chalkboard";
    public const string DoughTable = "dough-table";
    public const string WoodStack = "wood-stack";
    public const string FishTank = "fish-tank";
    public const string Jukebox = "jukebox";
    public const string DjBooth = "dj-booth";
    public const string BarStool = "bar-stool";
    public const string VelvetRope = "velvet-rope";
    public const string Podium = "podium";

    public static IReadOnlyList<string> All { get; } =
    [
        Tree, Bench, Lamp, FlowerBed, CafeTable, Counter, Plant, CatTree, Rug, Armchair, Sofa, FloorLamp, Bookshelf, CatBed,
        MapBoard, Piano, PottingBench, RodRack, PearTree, PackingCrate,
        CatalogueStand, HatShelf, ShopCounter, Mirror,
        MarketStall, PizzaTable, PizzaOven, Stage, SpeakerStack, LightRig, FishBarrel, YarnBasket, MarketCart, Chalkboard,
        DoughTable, WoodStack, FishTank, Jukebox, DjBooth, BarStool, VelvetRope, Podium,
    ];
}
