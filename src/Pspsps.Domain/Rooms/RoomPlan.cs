namespace Pspsps.Domain.Rooms;

/// <summary>Lay out public floors in metres by matching the two thresholds of every passage.</summary>
public static class RoomPlan
{
    public static IReadOnlyList<RoomDefinition> Join(RoomDefinition[] floors)
    {
        ArgumentNullException.ThrowIfNull(floors);
        if (floors.Length == 0)
        {
            throw new ArgumentException("A world needs a starting room.", nameof(floors));
        }
        const double tolerance = 0.000001;
        var byId = floors.ToDictionary(room => room.Id);
        var placed = new Dictionary<string, RoomDefinition> { [floors[0].Id] = floors[0] };
        var queue = new Queue<RoomDefinition>();
        queue.Enqueue(floors[0]);
        while (queue.TryDequeue(out var room))
        {
            foreach (var door in room.Doors)
            {
                var target = byId[door.ToRoom];
                var back = target.GetDoor(door.ToDoor ?? throw new InvalidOperationException("Missing return door."));
                if (back.ToRoom != room.Id || back.ToDoor != door.Id || Facing(room, door) != (Facing(target, back) + 2) % 4)
                {
                    throw new InvalidOperationException($"Mismatched thresholds at {room.Id}/{door.Id}.");
                }

                var exit = room.MapExit(door);
                var entrance = target.MapExit(back);
                var centre = new Position(target.MapCentre.X + exit.X - entrance.X, target.MapCentre.Z + exit.Z - entrance.Z);
                if (placed.TryGetValue(target.Id, out var existing))
                {
                    if (existing.MapCentre.DistanceTo(centre) > tolerance)
                    {
                        throw new InvalidOperationException($"The loop through {room.Id}/{door.Id} does not close.");
                    }
                    continue;
                }

                var neighbour = target with { MapCentre = centre };
                placed.Add(target.Id, neighbour);
                queue.Enqueue(neighbour);
            }
        }

        if (placed.Count != floors.Length)
        {
            throw new InvalidOperationException("Every public room needs a walking route from the square.");
        }
        var neighbours = placed.Values.ToArray();
        for (var i = 0; i < neighbours.Length; i++)
        {
            var a = neighbours[i];
            foreach (var b in neighbours.Skip(i + 1))
            {
                if (Math.Abs(a.MapCentre.X - b.MapCentre.X) + tolerance < (a.Width + b.Width) / 2 &&
                    Math.Abs(a.MapCentre.Z - b.MapCentre.Z) + tolerance < (a.Depth + b.Depth) / 2)
                {
                    throw new InvalidOperationException($"{a.Id} overlaps {b.Id}.");
                }
            }
        }

        return floors.Select(room => placed[room.Id]).ToArray();
    }

    private static int Facing(RoomDefinition room, Door door) =>
        ((int)(door.Edge ?? throw new InvalidOperationException("Missing floor edge.")) + (room.MapRotated ? 2 : 0)) % 4;
}
