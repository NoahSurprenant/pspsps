namespace Pspsps.Domain.Rooms;

/// <summary>A door as it is kept: on a floor edge, an offset across it from the edge's midpoint (<see cref="RoomDefinition.WithDoors"/>).</summary>
public sealed record DoorLayout(string Id, RoomEdge Edge, double Offset, string ToRoom, string ToDoor);

/// <summary>
/// A room as it is kept in the database and edited: everything of a <see cref="RoomDefinition"/> that is not worked out
/// (its ID is its key, and where it stands on the world map comes from its doors, <see cref="RoomPlan"/>).
/// </summary>
public sealed record RoomLayout(string Name, double Width, double Depth, Position Spawn, int Capacity)
{
    public Pond? Pond { get; init; }

    public Storefront? Exterior { get; init; }

    /// <summary>The ID of the look the browser paints the room in; null for the room's own (by its ID).</summary>
    public string? Look { get; init; }

    public IReadOnlyList<Prop> Props { get; init; } = [];

    public IReadOnlyList<DoorLayout> Doors { get; init; } = [];

    public static RoomLayout Of(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        return new RoomLayout(room.Name, room.Width, room.Depth, room.Spawn, room.Capacity)
        {
            Pond = room.Pond,
            Exterior = room.Exterior,
            Look = room.Look,
            Props = room.Props,
            Doors = [.. room.Doors.Select(door => DoorOf(room, door))],
        };
    }

    /// <summary>The room, without its place on the world map. Throws <see cref="ArgumentException"/> for a door that does not fit.</summary>
    public RoomDefinition ToDefinition(string id)
    {
        var room = new RoomDefinition(id, Name, Width, Depth, Spawn, Capacity, Pond)
        {
            Props = Props,
            Exterior = Exterior,
            Look = Look,
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
