namespace Pspsps.Domain.Rooms;

/// <summary>
/// A room's layout: a rectangular floor centred on the origin, where cats arrive, how many fit, a pond to fish in if it
/// has one, the doors out of it, and its props.
/// </summary>
public sealed record RoomDefinition(
    string Id, string Name, double Width, double Depth, Position Spawn, int Capacity, Pond? Pond = null)
{
    /// <summary>The doors out of this room, each to a door of another room.</summary>
    public IReadOnlyList<Door> Doors { get; init; } = [];

    /// <summary>The room's decoration, which cats walk through (<see cref="PropPlacement"/> has where props may stand).</summary>
    public IReadOnlyList<Prop> Props { get; init; } = [];

    /// <summary>The ID of the look the browser paints the room in (an overlay's season, say); null for the room's own.</summary>
    public string? Look { get; init; }

    /// <summary>The centre of a public room on the world map, in the same metres as its floor.</summary>
    public Position MapCentre { get; init; }

    /// <summary>For an interior, the building seen from the street has its own footprint and front entrance.</summary>
    public Storefront? Exterior { get; init; }

    /// <summary>Whether this is the same room as <paramref name="other"/> to a cat in it: the same floor, doors, props and look.</summary>
    public bool SameAs(RoomDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Equals(this with { Doors = [], Props = [] }, other with { Doors = [], Props = [] })
            && Doors.SequenceEqual(other.Doors) && Props.SequenceEqual(other.Props);
    }

    public double MapWidth => Exterior?.Width ?? Width;
    public double MapDepth => Exterior?.Depth ?? Depth;

    public RoomEdge MapEdge(Door door) =>
        Exterior?.Front ?? door.Edge ?? throw new ArgumentException("A map exit needs a floor edge.", nameof(door));

    /// <summary>Places each exit one metre inside its edge. Offsets run across the edge from its midpoint.</summary>
    public RoomDefinition WithDoors(params (string Id, RoomEdge Edge, double Offset, string ToRoom, string ToDoor)[] exits)
    {
        const double radius = 0.8;
        const double inset = 1;
        if (!double.IsFinite(Width) || !double.IsFinite(Depth) || Width < 2 * inset || Depth < 2 * inset)
        {
            throw new ArgumentOutOfRangeException(nameof(exits), $"The floor of {Id} is too small for an exit.");
        }

        var doors = new List<Door>(exits.Length);
        foreach (var exit in exits)
        {
            var halfEdge = exit.Edge is RoomEdge.North or RoomEdge.South ? Width / 2 : Depth / 2;
            if (!double.IsFinite(exit.Offset) || Math.Abs(exit.Offset) + radius > halfEdge)
            {
                throw new ArgumentOutOfRangeException(nameof(exits), $"Exit {exit.Id} does not fit on the {exit.Edge} edge of {Id}.");
            }

            var spot = PointOnEdge(exit.Edge, exit.Offset, inset, Width, Depth);
            if (doors.Any(door => door.Spot.DistanceTo(spot) <= door.Radius + radius))
            {
                throw new ArgumentException($"Exit {exit.Id} overlaps another exit in {Id}.", nameof(exits));
            }

            var door = new Door(exit.Id, spot, radius, exit.ToRoom, exit.ToDoor) { Edge = exit.Edge };
            if (Clamp(door.Arrival) != door.Arrival)
            {
                throw new ArgumentOutOfRangeException(nameof(exits), $"No landing inside {Id}/{exit.Id}.");
            }

            doors.Add(door);
        }

        return this with { Doors = doors };
    }

    /// <summary>The point where an exit reaches the floor boundary, converted to world-map coordinates.</summary>
    public Position MapExit(Door door)
    {
        var offset = MapExitOffset(door);
        return new Position(MapCentre.X + offset.X, MapCentre.Z + offset.Z);
    }

    internal Position MapExitOffset(Door door)
    {
        var edge = MapEdge(door);
        var offset = edge is RoomEdge.North or RoomEdge.South ? door.Spot.X : door.Spot.Z;
        return PointOnEdge(edge, Exterior is null ? offset : 0, 0, MapWidth, MapDepth);
    }

    private static Position PointOnEdge(RoomEdge edge, double offset, double inset, double width, double depth) => edge switch
    {
        RoomEdge.North => new Position(offset, depth / 2 - inset),
        RoomEdge.East => new Position(width / 2 - inset, offset),
        RoomEdge.South => new Position(offset, -depth / 2 + inset),
        RoomEdge.West => new Position(-width / 2 + inset, offset),
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    /// <summary>
    /// Whether the shop's catalogue stands here (a <see cref="PropKinds.CatalogueStand"/> among the props): the only kind
    /// of room where things are bought (<see cref="Players.Player.Buy"/>).
    /// </summary>
    public bool HasCatalogue => Props.Any(p => p.Kind == PropKinds.CatalogueStand);

    /// <summary>The nearest spot on the floor to <paramref name="target"/>.</summary>
    public Position Clamp(Position target) => new(
        Math.Clamp(target.X, -Width / 2, Width / 2),
        Math.Clamp(target.Z, -Depth / 2, Depth / 2));

    /// <summary>The door with this ID; a <see cref="DomainException"/> when the room has none such.</summary>
    public Door GetDoor(string doorId) =>
        Doors.FirstOrDefault(d => d.Id == doorId) ?? throw new DomainException("There's no such door here.");
}

public sealed record Storefront(double Width, double Depth, RoomEdge Front);
