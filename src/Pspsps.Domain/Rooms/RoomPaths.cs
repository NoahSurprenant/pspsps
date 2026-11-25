namespace Pspsps.Domain.Rooms;

/// <summary>Where the paths between placed rooms run, and whether they are sound: clear of every room and of each other, and each ending at another room.</summary>
public static class RoomPaths
{
    private const double Tolerance = 1e-9;

    /// <summary>The floor of a room on the world, as a box.</summary>
    public readonly record struct Box(double X, double Z, double Width, double Depth)
    {
        public double MinX => X - (Width / 2);

        public double MaxX => X + (Width / 2);

        public double MinZ => Z - (Depth / 2);

        public double MaxZ => Z + (Depth / 2);

        public bool Overlaps(Box other) =>
            MinX < other.MaxX - Tolerance && other.MinX < MaxX - Tolerance
            && MinZ < other.MaxZ - Tolerance && other.MinZ < MaxZ - Tolerance;
    }

    public static Box BoxOf(RoomDefinition room)
    {
        var place = room.Place ?? throw new InvalidOperationException($"{room.Id} has no place on the world map.");
        return new Box((place.X * WorldPlace.Cell) + (room.Width / 2), (place.Z * WorldPlace.Cell) + (room.Depth / 2), room.Width, room.Depth);
    }

    /// <summary>The strip of <paramref name="path"/>, leaving <paramref name="room"/>, on the world.</summary>
    public static Box BoxOf(RoomDefinition room, RoomPath path)
    {
        var box = BoxOf(room);
        var (x, z, width, depth) = path.Floor(box.X, box.Z, room.Width, room.Depth);
        return new Box(x, z, width, depth);
    }

    /// <summary>Whether the far end of the strip meets the edge of <paramref name="other"/>, the whole strip's width on it.</summary>
    public static bool Reaches(RoomDefinition room, RoomPath path, RoomDefinition other)
    {
        if (other.Place is null || other.Id == room.Id)
        {
            return false;
        }

        var strip = BoxOf(room, path);
        var target = BoxOf(other);
        return path.Edge switch
        {
            RoomEdge.North => Meets(strip.MaxZ, target.MinZ) && Within(strip.MinX, strip.MaxX, target.MinX, target.MaxX),
            RoomEdge.South => Meets(strip.MinZ, target.MaxZ) && Within(strip.MinX, strip.MaxX, target.MinX, target.MaxX),
            RoomEdge.East => Meets(strip.MaxX, target.MinX) && Within(strip.MinZ, strip.MaxZ, target.MinZ, target.MaxZ),
            _ => Meets(strip.MinX, target.MaxX) && Within(strip.MinZ, strip.MaxZ, target.MinZ, target.MaxZ),
        };
    }

    public static IEnumerable<string> Problems(IReadOnlyCollection<RoomDefinition> rooms)
    {
        var placed = rooms.Where(room => room.Place is not null).OrderBy(room => room.Id, StringComparer.Ordinal).ToList();
        foreach (var room in rooms.Where(room => room.Place is null && room.Paths.Count > 0))
        {
            yield return $"{room.Id}: has paths but no place on the world map.";
        }

        var strips = new List<(RoomDefinition Room, int Index, Box Box)>();
        foreach (var room in placed)
        {
            for (var index = 0; index < room.Paths.Count; index++)
            {
                var path = room.Paths[index];
                var name = $"{room.Id}/path {index + 1}";
                var half = path.Edge is RoomEdge.North or RoomEdge.South ? room.Width / 2 : room.Depth / 2;
                if (!double.IsFinite(path.Width) || !double.IsFinite(path.Length) || !double.IsFinite(path.Offset) || path.Width <= 0 || path.Length <= 0)
                {
                    yield return $"{name}: has no size.";
                }
                else if (Math.Abs(path.Offset) + (path.Width / 2) > half + Tolerance)
                {
                    yield return $"{name}: doesn't fit on the {path.Edge} edge.";
                }
                else
                {
                    strips.Add((room, index, BoxOf(room, path)));
                }
            }
        }

        foreach (var (room, index, box) in strips)
        {
            var name = $"{room.Id}/path {index + 1}";
            var path = room.Paths[index];
            foreach (var other in placed.Where(other => BoxOf(other).Overlaps(box)))
            {
                yield return $"{name}: runs over {other.Id}.";
            }

            foreach (var (otherRoom, otherIndex, _) in strips.Where(s => (s.Room.Id, s.Index).CompareTo((room.Id, index)) > 0 && s.Box.Overlaps(box)))
            {
                yield return $"{name}: crosses {otherRoom.Id}/path {otherIndex + 1}.";
            }

            if (!placed.Any(other => Reaches(room, path, other)))
            {
                yield return $"{name}: doesn't end at another room.";
            }
        }
    }

    private static bool Meets(double a, double b) => Math.Abs(a - b) < Tolerance;

    private static bool Within(double min, double max, double outerMin, double outerMax) =>
        min >= outerMin - Tolerance && max <= outerMax + Tolerance;
}
