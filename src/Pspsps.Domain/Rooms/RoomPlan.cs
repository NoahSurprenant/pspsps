namespace Pspsps.Domain.Rooms;

/// <summary>Checks that the public rooms are one world: every room can be walked to from the first one, through doors that lead back, or, for rooms on the world grid, across an edge they share.</summary>
public static class RoomPlan
{
    public static IReadOnlyList<RoomDefinition> Join(RoomDefinition[] floors)
    {
        ArgumentNullException.ThrowIfNull(floors);
        if (floors.Length == 0)
        {
            throw new ArgumentException("A world needs a starting room.", nameof(floors));
        }

        var byId = floors.ToDictionary(room => room.Id);
        var reached = new HashSet<string> { floors[0].Id };
        var queue = new Queue<RoomDefinition>();
        queue.Enqueue(floors[0]);
        while (queue.TryDequeue(out var room))
        {
            foreach (var door in room.Doors)
            {
                var target = byId[door.ToRoom];
                var back = target.GetDoor(door.ToDoor ?? throw new InvalidOperationException("Missing return door."));
                if (back.ToRoom != room.Id || back.ToDoor != door.Id)
                {
                    throw new InvalidOperationException($"Mismatched thresholds at {room.Id}/{door.Id}.");
                }

                if (reached.Add(target.Id))
                {
                    queue.Enqueue(target);
                }
            }

            foreach (var neighbour in floors.Where(other => Touches(room, other)))
            {
                if (reached.Add(neighbour.Id))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        if (reached.Count != floors.Length)
        {
            throw new InvalidOperationException("Every public room needs a walking route from the square.");
        }

        return floors;
    }

    private static bool Meets(double a, double b) => Math.Abs(a - b) < 1e-9;

    private static bool Touches(RoomDefinition a, RoomDefinition b)
    {
        if (a.Place is not { } pa || b.Place is not { } pb || a.Id == b.Id)
        {
            return false;
        }

        var (ax, az, bx, bz) = (pa.X * WorldPlace.Cell, pa.Z * WorldPlace.Cell, pb.X * WorldPlace.Cell, pb.Z * WorldPlace.Cell);
        var overlapX = ax < bx + b.Width && bx < ax + a.Width;
        var overlapZ = az < bz + b.Depth && bz < az + a.Depth;
        return (overlapX && (Meets(bz, az + a.Depth) || Meets(bz + b.Depth, az))) || (overlapZ && (Meets(bx, ax + a.Width) || Meets(bx + b.Width, ax)));
    }
}
