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
    public const string Goal = "goal";
    public const string HockeyNet = "hockey-net";
    public const string IcePatch = "ice-patch";
    public const string RinkBoard = "rink-board";
    public const string YarnBall = "yarn-ball";
    public const string SnowCat = "snow-cat";
    public const string BookStack = "book-stack";
    public const string Globe = "globe";
    public const string YarnNook = "yarn-nook";
    public const string WindowSeat = "window-seat";
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
    public const string Fountain = "fountain";
    public const string ShoreLighthouse = "shore-lighthouse";
    public const string ShoreDeckChair = "shore-deck-chair";
    public const string ShoreParasol = "shore-parasol";
    public const string ShoreRocks = "shore-rocks";
    public const string ShoreRowboat = "shore-rowboat";
    public const string ShoreSurfboards = "shore-surfboards";
    public const string ShorePicnicTable = "shore-picnic-table";
    public const string ShoreTubes = "shore-tubes";
    public const string ShoreBollard = "shore-bollard";
    public const string ShoreLifebuoy = "shore-lifebuoy";
    public const string ShoreLiftTower = "shore-lift-tower";
    public const string ShoreLiftHut = "shore-lift-hut";
    public const string ShorePost = "shore-post";
    public const string ShorePine = "shore-pine";
    public const string ShoreLodge = "shore-lodge";
    public const string ShoreTourStand = "shore-tour-stand";
    public const string ShoreSignpost = "shore-signpost";
    public const string FieldSandcastle = "field-sandcastle";
    public const string FieldSandcastleSmall = "field-sandcastle-small";
    public const string FieldBucket = "field-bucket";
    public const string FieldSapling = "field-sapling";
    public const string FieldPine = "field-pine";
    public const string FieldBush = "field-bush";
    public const string FieldRock = "field-rock";
    public const string FieldLog = "field-log";
    public const string FieldStump = "field-stump";
    public const string FieldSurfShack = "field-surf-shack";
    public const string FieldBonfire = "field-bonfire";
    public const string FieldTowel = "field-towel";
    public const string FieldUmbrella = "field-umbrella";
    public const string FieldSurfboards = "field-surfboards";
    public const string FieldLifeguardChair = "field-lifeguard-chair";
    public const string FieldCornerFlag = "field-corner-flag";
    public const string FieldBall = "field-ball";
    public const string FieldCone = "field-cone";
    public const string FieldSignpost = "field-signpost";
    public const string FieldBoulder = "field-boulder";
    public const string FieldTent = "field-tent";
    public const string FieldTentBlue = "field-tent-blue";
    public const string FieldFence = "field-fence";
    public const string FieldHedge = "field-hedge";
    public const string FieldFlowers = "field-flowers";
    public const string FieldMushrooms = "field-mushrooms";
    public const string FieldHayBale = "field-hay-bale";
    public const string FieldFlagPole = "field-flag-pole";
    public const string FieldPalisade = "field-palisade";
    public const string FieldStoneWall = "field-stone-wall";
    public const string FieldWaterfall = "field-waterfall";
    public const string FieldBigTree = "field-big-tree";
    public const string FieldFootbridge = "field-footbridge";
    public const string FieldArrowSign = "field-arrow-sign";
    public const string FieldBigRock = "field-big-rock";

    public static IReadOnlyList<string> All { get; } =
    [
        Tree, Bench, Lamp, FlowerBed, CafeTable, Counter, Plant, CatTree, Rug, Armchair, Sofa, FloorLamp, Bookshelf, CatBed,
        MapBoard, Piano, PottingBench, RodRack, PearTree, PackingCrate,
        Goal, HockeyNet, IcePatch, RinkBoard, YarnBall, SnowCat,
        CatalogueStand, HatShelf, ShopCounter, Mirror, BookStack, Globe, YarnNook, WindowSeat,
        MarketStall, PizzaTable, PizzaOven, Stage, SpeakerStack, LightRig, FishBarrel, YarnBasket, MarketCart, Chalkboard,
        DoughTable, WoodStack, FishTank, Jukebox, DjBooth, BarStool, VelvetRope, Podium, Fountain,
        ShoreLighthouse, ShoreDeckChair, ShoreParasol, ShoreRocks, ShoreRowboat, ShoreSurfboards, ShorePicnicTable, ShoreTubes, ShoreBollard, ShoreLifebuoy, ShoreLiftTower, ShoreLiftHut, ShorePost, ShorePine, ShoreLodge, ShoreTourStand, ShoreSignpost,
        FieldSandcastle, FieldSandcastleSmall, FieldBucket, FieldSapling, FieldPine, FieldBush, FieldRock, FieldLog, FieldStump,
        FieldSurfShack, FieldBonfire, FieldTowel, FieldUmbrella, FieldSurfboards, FieldLifeguardChair, FieldCornerFlag, FieldBall,
        FieldCone, FieldSignpost,
        FieldBoulder, FieldTent, FieldTentBlue, FieldFence, FieldHedge, FieldFlowers, FieldMushrooms, FieldHayBale, FieldFlagPole, FieldPalisade, FieldStoneWall,
        FieldWaterfall, FieldBigTree, FieldFootbridge, FieldArrowSign, FieldBigRock,
    ];
}
