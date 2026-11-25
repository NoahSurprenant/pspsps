namespace Pspsps.Domain.Rooms;

/// <summary>A door as it is kept: on a floor edge, an offset across it from the edge's midpoint (<see cref="RoomDefinition.WithDoors"/>).</summary>
public sealed record DoorLayout(string Id, RoomEdge Edge, double Offset, string ToRoom, string ToDoor);

/// <summary>
/// A room as it is kept in the database and edited: everything of a <see cref="RoomDefinition"/> that is not worked out
/// (its ID is its key).
/// </summary>
public sealed record RoomLayout(string Name, double Width, double Depth, Position Spawn, int Capacity)
{
    public Pond? Pond { get; init; }

    /// <summary>The ID of the look the browser paints the room in; null for the room's own (by its ID).</summary>
    public string? Look { get; init; }

    public IReadOnlyList<Prop> Props { get; init; } = [];

    public IReadOnlyList<DoorLayout> Doors { get; init; } = [];

    /// <summary>Where the room sits on the world grid, for an outdoor room that cats can walk out of into its neighbours; null for an indoor room.</summary>
    public WorldPlace? Place { get; init; }

    /// <summary>The narrow paths out of the room's edges, to rooms that don't touch it (only for a placed room).</summary>
    public IReadOnlyList<RoomPath> Paths { get => _paths; init => _paths = value ?? []; }

    private readonly IReadOnlyList<RoomPath> _paths = [];

    public static RoomLayout Of(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        return new RoomLayout(room.Name, room.Width, room.Depth, room.Spawn, room.Capacity)
        {
            Pond = room.Pond,
            Look = room.Look,
            Props = room.Props,
            Place = room.Place,
            Paths = room.Paths,
            Doors = [.. room.Doors.Select(door => DoorOf(room, door))],
        };
    }

    /// <summary>The room. Throws <see cref="ArgumentException"/> for a door that does not fit.</summary>
    public RoomDefinition ToDefinition(string id)
    {
        var room = new RoomDefinition(id, Name, Width, Depth, Spawn, Capacity, Pond)
        {
            Props = Props,
            Look = Look,
            Place = Place,
            Paths = Paths,
        };
        return room.WithDoors([.. Doors.Select(door => (door.Id, door.Edge, door.Offset, door.ToRoom, door.ToDoor))]);
    }

    private static DoorLayout DoorOf(RoomDefinition room, Door door)
    {
        var edge = door.Edge ?? throw new ArgumentException($"The door {room.Id}/{door.Id} is on no floor edge.", nameof(room));
        var offset = edge is RoomEdge.North or RoomEdge.South ? door.Spot.X : door.Spot.Z;
        var toDoor = door.ToDoor ?? throw new ArgumentException($"The door {room.Id}/{door.Id} has no door to arrive by.", nameof(room));
        return new DoorLayout(door.Id, edge, offset, door.ToRoom, toDoor);
    }
}
