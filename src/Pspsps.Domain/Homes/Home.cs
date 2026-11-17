using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;

namespace Pspsps.Domain.Homes;

/// <summary>
/// A player's home: a room of their own, not in any world's catalogue, with the furniture they put in it and who may
/// visit. An aggregate of its own, keyed by its owner. Every home has the same floor plan (<see cref="Layout"/>), with
/// a door out to Town Square. Furniture goes only where the owner has it to put (each piece out at most as many times
/// as they have it), on the floor, out of the way of the door, the spawn point and the walks between them, off the
/// other furniture (<see cref="PropPlacement"/>, the rooms' own rules), and faces one of four ways.
/// </summary>
public sealed class Home
{
    /// <summary>How many pieces of furniture a home holds.</summary>
    public const int MaxPieces = 40;

    /// <summary>How many ways a piece can face: it turns a quarter at a time.</summary>
    public const int Turns = 4;

    /// <summary>The door out of every home: to the spawn point of Town Square, in the world the cat came from.</summary>
    public const string DoorOut = "out";

    private readonly List<PlacedFurniture> _furniture = [];

    private Home()
    {
    }

    private Home(Guid ownerId)
    {
        OwnerId = ownerId;
        Visitors = HomeVisitors.Friends;
    }

    /// <summary>
    /// Every home's floor: a cosy room, a door out by the back wall on the left, and the spot cats arrive at just in
    /// front of it, which leaves the rest of the floor for furniture.
    /// </summary>
    public static RoomDefinition Layout { get; } =
        new("home", "Home", Width: 12, Depth: 9, Spawn: new Position(-3, 1.6), Capacity: 20)
        {
            Doors = [new Door(DoorOut, new Position(-4.4, 3.7), Radius: 0.8, ToRoom: "town-square", ToDoor: null)],
        };

    public Guid OwnerId { get; private set; }

    public HomeVisitors Visitors { get; private set; }

    /// <summary>Counts every change: two changes decided at once on the same home can't both be saved.</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<PlacedFurniture> Furniture => _furniture.AsReadOnly();

    /// <summary>A player's home as it is before they change anything: no furniture out, and open to their friends.</summary>
    public static Home Create(Guid ownerId) =>
        ownerId == Guid.Empty ? throw new DomainException("That cat doesn't exist.") : new Home(ownerId);

    /// <summary>The way a piece turned <paramref name="quarterTurns"/> quarters faces, in radians (a prop's facing).</summary>
    public static double FacingOf(int quarterTurns) => quarterTurns * Math.PI / 2;

    /// <summary>The home as a room, called after its owner (<paramref name="ownerName"/>), with its furniture.</summary>
    public RoomDefinition Room(string ownerName) => RoomOf(ownerName, _furniture);

    /// <summary>What a home is called: after its owner (<paramref name="ownerName"/>).</summary>
    public static string NameOf(string ownerName) => $"{ownerName}'s home";

    /// <summary>A home as a room: called after its owner (<paramref name="ownerName"/>), with <paramref name="furniture"/>.</summary>
    public static RoomDefinition RoomOf(string ownerName, IEnumerable<PlacedFurniture> furniture)
    {
        ArgumentNullException.ThrowIfNull(furniture);
        return Layout with { Name = NameOf(ownerName), Props = [.. furniture.Select(piece => piece.AsProp())] };
    }

    /// <summary>
    /// Puts out a piece of the furniture <paramref name="itemId"/>, of which the owner has <paramref name="owned"/>, at
    /// <paramref name="spot"/>, turned <paramref name="quarterTurns"/> quarters.
    /// </summary>
    public PlacedFurniture Place(string itemId, int owned, Position spot, int quarterTurns, DateTimeOffset now)
    {
        var item = EnsureCanPutOut(itemId, owned);
        if (quarterTurns is < 0 or >= Turns)
        {
            throw new DomainException("Furniture turns a quarter at a time.");
        }

        var problems = PropPlacement.ProblemsOf(Layout, new Prop(item.Id, spot, item.Radius, FacingOf(quarterTurns)), Props());
        if (problems.Count > 0)
        {
            throw new DomainException(Says(problems[0]));
        }

        return Add(item, spot, quarterTurns, now);
    }

    /// <summary>
    /// Puts out a piece of the furniture <paramref name="itemId"/> wherever it fits, nearest the middle of the room: for
    /// a player who can't point at the floor (the keyboard).
    /// </summary>
    public PlacedFurniture PlaceAnywhere(string itemId, int owned, DateTimeOffset now)
    {
        var item = EnsureCanPutOut(itemId, owned);
        var props = Props();
        const double step = 0.25;
        var (halfWidth, halfDepth) = (Layout.Width / 2 - item.Radius, Layout.Depth / 2 - item.Radius);
        var spots = new List<Position>();
        for (var x = -Math.Floor(halfWidth / step) * step; x <= halfWidth; x += step)
        {
            for (var z = -Math.Floor(halfDepth / step) * step; z <= halfDepth; z += step)
            {
                spots.Add(new Position(Math.Round(x, 2), Math.Round(z, 2)));
            }
        }

        var middle = new Position(0, 0);
        var spot = spots
            .OrderBy(s => s.DistanceTo(middle))
            .ThenBy(s => s.X)
            .ThenBy(s => s.Z)
            .Where(s => PropPlacement.ProblemsOf(Layout, new Prop(item.Id, s, item.Radius), props).Count == 0)
            .Select(s => (Position?)s)
            .FirstOrDefault()
            ?? throw new DomainException($"There's no room left for the {item.Name}. Put something away first.");
        return Add(item, spot, 0, now);
    }

    /// <summary>Turns a piece a quarter (it takes up the same floor whichever way it faces).</summary>
    public PlacedFurniture Turn(Guid pieceId)
    {
        var piece = Get(pieceId);
        piece.Turn();
        Version++;
        return piece;
    }

    /// <summary>Puts a piece away: the owner still has it, to put out again.</summary>
    public void PutAway(Guid pieceId)
    {
        _furniture.Remove(Get(pieceId));
        Version++;
    }

    /// <summary>Changes who may visit. Cats already here who no longer may are sent out (<see cref="MayVisit"/>).</summary>
    public void LetIn(HomeVisitors visitors)
    {
        if (!Enum.IsDefined(visitors))
        {
            throw new DomainException("That isn't a choice.");
        }

        Visitors = visitors;
        Version++;
    }

    /// <summary>
    /// Whether <paramref name="playerId"/> may come in (or stay): the owner always; anyone else as the owner chose,
    /// where <paramref name="areFriends"/> says whether they are the owner's friends.
    /// </summary>
    public bool MayVisit(Guid playerId, bool areFriends) =>
        playerId == OwnerId || Visitors switch
        {
            HomeVisitors.Anyone => true,
            HomeVisitors.Friends => areFriends,
            _ => false,
        };

    /// <summary>Checks that <paramref name="playerId"/> may come in (<see cref="MayVisit"/>), saying why not.</summary>
    public void EnsureCanVisit(Guid playerId, bool areFriends)
    {
        if (MayVisit(playerId, areFriends))
        {
            return;
        }

        throw new DomainException(Visitors == HomeVisitors.Nobody
            ? "That home isn't open to visitors right now."
            : "Only friends can visit that home.");
    }

    private ShopItem EnsureCanPutOut(string itemId, int owned)
    {
        var item = ShopCatalog.Find(itemId);
        if (item is not { IsFurniture: true })
        {
            throw new DomainException("Only furniture goes in a home.");
        }

        if (owned <= 0)
        {
            throw new DomainException($"Buy the {item.Name} first.");
        }

        if (_furniture.Count(piece => piece.ItemId == item.Id) >= owned)
        {
            throw new DomainException($"Every {item.Name} you have is out already.");
        }

        if (_furniture.Count >= MaxPieces)
        {
            throw new DomainException("Your home is full. Put something away first.");
        }

        return item;
    }

    private PlacedFurniture Add(ShopItem item, Position spot, int quarterTurns, DateTimeOffset now)
    {
        var piece = new PlacedFurniture(Guid.CreateVersion7(now), item.Id, spot, quarterTurns);
        _furniture.Add(piece);
        Version++;
        return piece;
    }

    private List<Prop> Props() => [.. _furniture.Select(piece => piece.AsProp())];

    private PlacedFurniture Get(Guid pieceId) =>
        _furniture.FirstOrDefault(piece => piece.Id == pieceId)
        ?? throw new DomainException("That isn't in your home any more.");

    private static string Says(PropProblem problem) => problem.Kind switch
    {
        PropProblemKind.OffFloor => "That won't fit there: it would stick out of the room.",
        PropProblemKind.InTheWay => "Keep the way to the door clear.",
        PropProblemKind.Overlaps => "There's something in the way there.",
        _ => throw new InvalidOperationException($"A shop item can't be put out: {problem.Says}."),
    };
}
