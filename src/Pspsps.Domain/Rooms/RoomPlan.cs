namespace Pspsps.Domain.Rooms;

/// <summary>Checks that the public rooms are one world: every room can be walked to from the first one, through doors that lead back.</summary>
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
        }

        if (reached.Count != floors.Length)
        {
            throw new InvalidOperationException("Every public room needs a walking route from the square.");
        }

        return floors;
    }
}
