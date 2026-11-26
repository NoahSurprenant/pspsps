namespace Pspsps.Domain.Rooms;

/// <summary>
/// The outdoor world as one floor: every room with a <see cref="WorldPlace"/>, laid on the grid, so a spot is in world
/// metres (east, then south, from the grid's origin) and the floor is the union of the rooms' rectangles. A room's own
/// positions are metres from its centre; <see cref="ToWorld"/> and <see cref="ToLocal"/> convert.
/// </summary>
public sealed class WorldSpace
{
    /// <summary>How far a cat sees, and hears: wider than the screen at the furthest zoom.</summary>
    public const double ViewDistance = 40;

    /// <summary>How many other cats a player is shown at most: the nearest ones within <see cref="ViewDistance"/>.</summary>
    public const int MaxVisibleCats = 60;

    private const double WalkStep = 0.1;

    private readonly List<RoomDefinition> _rooms;

    public WorldSpace(IEnumerable<RoomDefinition> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        _rooms = [.. rooms.Where(room => room.Place is not null).OrderBy(room => room.Id, StringComparer.Ordinal)];
    }

    public IReadOnlyList<RoomDefinition> Rooms => _rooms;

    public bool IsEmpty => _rooms.Count == 0;

    /// <summary>Whether this room is part of the world (it has a place on the grid).</summary>
    public bool Has(string roomId) => _rooms.Any(room => room.Id == roomId);

    /// <summary>The world spot of <paramref name="local"/>, a position in the room <paramref name="roomId"/>.</summary>
    public Position ToWorld(string roomId, Position local)
    {
        var room = Find(roomId);
        var (cx, cz) = CentreOf(room);
        return new Position(cx + local.X, cz + local.Z);
    }

    /// <summary>The position of <paramref name="world"/> in the room <paramref name="roomId"/>, in metres from its centre.</summary>
    public Position ToLocal(string roomId, Position world)
    {
        var room = Find(roomId);
        var (cx, cz) = CentreOf(room);
        return new Position(world.X - cx, world.Z - cz);
    }

    /// <summary>Whether <paramref name="world"/> is on the floor of some room, or on a path between rooms.</summary>
    public bool Contains(Position world) => RoomAt(world) is not null;

    /// <summary>The room whose floor <paramref name="world"/> is on; where rooms touch, the first by ID. On a path, the room it leaves.</summary>
    public RoomDefinition? RoomAt(Position world) =>
        _rooms.FirstOrDefault(room => Clamp(room, world) == world)
        ?? _rooms.FirstOrDefault(room => room.Paths.Any(path => ClampTo(RoomPaths.BoxOf(room, path), world) == world));

    /// <summary>
    /// The spot on the world's floor nearest <paramref name="target"/>: itself when it is on the floor, else the closest
    /// point of the closest room (the first by ID when two are as close). Throws when no room is placed.
    /// </summary>
    public Position Clamp(Position target)
    {
        if (_rooms.Count == 0)
        {
            throw new InvalidOperationException("No room is on the world map.");
        }

        var best = Clamp(_rooms[0], target);
        var bestDistance = best.DistanceTo(target);
        foreach (var box in _rooms.Skip(1).Select(RoomPaths.BoxOf).Concat(_rooms.SelectMany(room => room.Paths.Select(path => RoomPaths.BoxOf(room, path)))))
        {
            var candidate = ClampTo(box, target);
            var distance = candidate.DistanceTo(target);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Where a cat that sets out from <paramref name="from"/> straight towards <paramref name="target"/> ends up: at the
    /// target when the whole line is on the world's floor, else at the last spot of the floor on that line (the cat
    /// walks up to the edge and stops, never across the grass or buildings between rooms).
    /// </summary>
    public Position WalkToward(Position from, Position target)
    {
        if (!Contains(from))
        {
            return Clamp(target);
        }

        var length = from.DistanceTo(target);
        var steps = (int)Math.Ceiling(length / WalkStep);
        var good = 0.0;
        for (var step = 1; step <= steps; step++)
        {
            var t = Math.Min(1.0, step * WalkStep / length);
            if (!Contains(Along(from, target, t)))
            {
                var bad = t;
                for (var i = 0; i < 20; i++)
                {
                    var middle = (good + bad) / 2;
                    if (Contains(Along(from, target, middle)))
                    {
                        good = middle;
                    }
                    else
                    {
                        bad = middle;
                    }
                }

                return Along(from, target, good);
            }

            good = t;
        }

        return target;
    }

    /// <summary>The door <paramref name="doorId"/> of the room <paramref name="roomId"/>, where it is in the world.</summary>
    public Position DoorSpot(string roomId, string doorId) => ToWorld(roomId, Find(roomId).GetDoor(doorId).Spot);

    private static Position Along(Position from, Position to, double t) =>
        new(from.X + ((to.X - from.X) * t), from.Z + ((to.Z - from.Z) * t));

    private RoomDefinition Find(string roomId) =>
        _rooms.FirstOrDefault(room => room.Id == roomId)
        ?? throw new DomainException($"{roomId} isn't on the world map.");

    private static (double X, double Z) CentreOf(RoomDefinition room)
    {
        var place = room.Place ?? throw new InvalidOperationException($"{room.Id} has no place on the world map.");
        return ((place.X * WorldPlace.Cell) + (room.Width / 2), (place.Z * WorldPlace.Cell) + (room.Depth / 2));
    }

    private static Position Clamp(RoomDefinition room, Position world) => ClampTo(RoomPaths.BoxOf(room), world);

    private static Position ClampTo(RoomPaths.Box box, Position world) =>
        new(Math.Clamp(world.X, box.MinX, box.MaxX), Math.Clamp(world.Z, box.MinZ, box.MaxZ));
}
