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

    /// <summary>The centre of a public room on the world map, in the same metres as its floor.</summary>
    public Position MapCentre { get; init; }

    /// <summary>Indoors the camera looks back toward the entrance, so the floor faces the opposite way on the map.</summary>
    public bool MapRotated { get; init; }

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

            var spot = PointOnEdge(exit.Edge, exit.Offset, inset);
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
        var edge = door.Edge ?? throw new ArgumentException("A map exit needs a floor edge.", nameof(door));
        var offset = edge is RoomEdge.North or RoomEdge.South ? door.Spot.X : door.Spot.Z;
        var point = PointOnEdge(edge, offset, inset: 0);
        var scale = MapRotated ? -1 : 1;
        return new Position(MapCentre.X + (point.X * scale), MapCentre.Z + (point.Z * scale));
    }

    private Position PointOnEdge(RoomEdge edge, double offset, double inset) => edge switch
    {
        RoomEdge.North => new Position(offset, Depth / 2 - inset),
        RoomEdge.East => new Position(Width / 2 - inset, offset),
        RoomEdge.South => new Position(offset, -Depth / 2 + inset),
        RoomEdge.West => new Position(-Width / 2 + inset, offset),
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
