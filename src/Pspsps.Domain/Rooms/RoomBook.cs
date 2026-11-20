namespace Pspsps.Domain.Rooms;

/// <summary>
/// Every public room of a world with its overlays, checked as a whole: that the doors meet, that the rooms fit together
/// on the world map without overlapping, that every prop keeps clear (<see cref="PropPlacement"/>) and that every
/// overlay does too (<see cref="RoomOverlays"/>). A room is published only into a set that has no problems.
/// </summary>
public sealed class RoomBook
{
    /// <summary>The room cats arrive in, and the one the world map is laid out from.</summary>
    public const string StartingRoom = "town-square";

    private readonly Dictionary<string, RoomDefinition> _byId;

    private RoomBook(IReadOnlyList<RoomDefinition> rooms, IReadOnlyList<RoomOverlay> overlays)
    {
        Rooms = rooms;
        Overlays = overlays;
        _byId = rooms.ToDictionary(room => room.Id);
    }

    /// <summary>The rooms as they are without overlays, the starting room first.</summary>
    public IReadOnlyList<RoomDefinition> Rooms { get; }

    public IReadOnlyList<RoomOverlay> Overlays { get; }

    /// <summary>The room as it is at <paramref name="now"/>, with the overlays that are on; null when there is no such room.</summary>
    public RoomDefinition? Find(string id, DateTimeOffset now) =>
        _byId.TryGetValue(id, out var room) ? RoomOverlays.Apply(room, Overlays, now) : null;

    /// <summary>The room with no overlays on it; null when there is no such room.</summary>
    public RoomDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<RoomDefinition> At(DateTimeOffset now) => [.. Rooms.Select(room => RoomOverlays.Apply(room, Overlays, now))];

    public static RoomBook Create(IReadOnlyDictionary<string, RoomLayout> layouts, IReadOnlyList<RoomOverlay> overlays)
    {
        var problems = Problems(layouts, overlays, out var rooms);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        return new RoomBook(rooms, overlays);
    }

    /// <summary>What is wrong with the rooms and overlays as a set, one line per problem; empty when they are fine.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyDictionary<string, RoomLayout> layouts, IReadOnlyList<RoomOverlay> overlays) =>
        Problems(layouts, overlays, out _);

    private static List<string> Problems(
        IReadOnlyDictionary<string, RoomLayout> layouts, IReadOnlyList<RoomOverlay> overlays, out RoomDefinition[] rooms)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(overlays);
        var problems = new List<string>();
        rooms = [];
        if (!layouts.ContainsKey(StartingRoom))
        {
            problems.Add($"There is no {StartingRoom}, the room cats arrive in.");
            return problems;
        }

        var defined = new Dictionary<string, RoomDefinition>();
        foreach (var (id, layout) in layouts.OrderBy(pair => pair.Key == StartingRoom ? 0 : 1).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var basics = Basics(id, layout).ToList();
            problems.AddRange(basics);
            if (basics.Count > 0)
            {
                continue;
            }

            try
            {
                defined.Add(id, layout.ToDefinition(id));
            }
            catch (ArgumentException exception)
            {
                problems.Add($"{id}: {exception.Message}");
            }
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        foreach (var room in defined.Values)
        {
            foreach (var door in room.Doors)
            {
                if (!defined.TryGetValue(door.ToRoom, out var target))
                {
                    problems.Add($"{room.Id}/{door.Id}: leads to {door.ToRoom}, which there is no such room.");
                }
                else if (target.Doors.All(back => back.Id != door.ToDoor))
                {
                    problems.Add($"{room.Id}/{door.Id}: leads to {door.ToRoom}/{door.ToDoor}, which there is no such door.");
                }
            }
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        try
        {
            rooms = [.. RoomPlan.Join([.. defined.Values])];
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or DomainException)
        {
            problems.Add(exception.Message);
            return problems;
        }

        foreach (var room in rooms)
        {
            problems.AddRange(PropPlacement.Problems(room));
            problems.AddRange(RoomOverlays.Problems(room, [.. overlays.Where(overlay => overlay.RoomId == room.Id)]));
        }

        problems.AddRange(overlays
            .Where(overlay => !defined.ContainsKey(overlay.RoomId))
            .Select(overlay => $"{overlay.RoomId}/{overlay.Id}: is an overlay of a room that there is not."));
        return problems;
    }

    private static IEnumerable<string> Basics(string id, RoomLayout layout)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
        {
            yield return $"{id}: a room's ID is lower-case letters, digits and hyphens.";
        }

        if (string.IsNullOrWhiteSpace(layout.Name))
        {
            yield return $"{id}: has no name.";
        }

        if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Depth) || layout.Width <= 0 || layout.Depth <= 0)
        {
            yield return $"{id}: its floor has no size.";
        }

        if (layout.Capacity <= 0)
        {
            yield return $"{id}: holds no cats.";
        }
    }
}
