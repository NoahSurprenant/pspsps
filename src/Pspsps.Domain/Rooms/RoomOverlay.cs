namespace Pspsps.Domain.Rooms;

/// <summary>
/// When an overlay is on. A one-off window runs from <see cref="Starts"/> up to, not including, <see cref="Ends"/>. A
/// yearly one repeats every year, from the month and day of <see cref="Starts"/> up to, not including, those of
/// <see cref="Ends"/> (the years are ignored, and the window may run over New Year), all in UTC.
/// </summary>
public sealed record OverlayWindow(DateTimeOffset Starts, DateTimeOffset Ends, bool Yearly = false)
{
    public bool Covers(DateTimeOffset now)
    {
        if (!Yearly)
        {
            return now >= Starts && now < Ends;
        }

        var from = MonthDay(Starts);
        var to = MonthDay(Ends);
        var today = MonthDay(now);
        return from <= to ? today >= from && today < to : today >= from || today < to;
    }

    /// <summary>What is wrong with the window; null when it is fine.</summary>
    public string? Problem => Yearly
        ? MonthDay(Starts) == MonthDay(Ends) ? "starts and ends on the same day of the year" : null
        : Ends <= Starts ? "ends before it starts" : null;

    private static int MonthDay(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return (utc.Month * 100) + utc.Day;
    }
}

/// <summary>
/// A change to a room for a while: a season's decorations, a party. While its <see cref="Window"/> is open the room has
/// the <see cref="Adds"/> as well as its own props, lacks the <see cref="Removes"/> (found among the room's props by kind
/// and spot), and, when there is a <see cref="Look"/>, is painted in that look instead of its own.
/// </summary>
public sealed record RoomOverlay(string Id, string RoomId, string Name, OverlayWindow Window)
{
    public IReadOnlyList<Prop> Adds { get; init; } = [];

    public IReadOnlyList<Prop> Removes { get; init; } = [];

    /// <summary>The ID of a look the room is painted in meanwhile (for the browser to find); null for the room's own.</summary>
    public string? Look { get; init; }
}

public static class RoomOverlays
{
    /// <summary>The room as it is at <paramref name="now"/>: with every one of its overlays that is on laid over it, the earliest first.</summary>
    public static RoomDefinition Apply(RoomDefinition room, IEnumerable<RoomOverlay> overlays, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(overlays);
        var active = overlays
            .Where(overlay => overlay.RoomId == room.Id && overlay.Window.Covers(now))
            .OrderBy(overlay => overlay.Window.Starts)
            .ThenBy(overlay => overlay.Id, StringComparer.Ordinal)
            .ToArray();
        return active.Length == 0 ? room : Lay(room, active);
    }

    /// <summary>
    /// What is wrong with the room's overlays, one line per problem; empty when they are all fine. Each is checked on
    /// its own, and all together (the worst a room can be: every overlay open at once), with the rules for props that
    /// hold for the room itself (<see cref="PropPlacement"/>): only what the overlays themselves bring is reported.
    /// </summary>
    public static IReadOnlyList<string> Problems(RoomDefinition room, IReadOnlyList<RoomOverlay> overlays)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(overlays);
        var problems = new List<string>();
        var own = PropPlacement.Problems(room).ToHashSet();
        var ids = new HashSet<string>();
        foreach (var overlay in overlays)
        {
            var name = $"{room.Id}/{overlay.Id}";
            if (overlay.RoomId != room.Id)
            {
                problems.Add($"{name}: is an overlay of {overlay.RoomId}, not of this room");
                continue;
            }

            if (string.IsNullOrWhiteSpace(overlay.Id) || string.IsNullOrWhiteSpace(overlay.Name))
            {
                problems.Add($"{name}: has no ID or no name");
            }

            if (!ids.Add(overlay.Id))
            {
                problems.Add($"{name}: is not the only overlay of the room with this ID");
            }

            if (overlay.Window.Problem is { } window)
            {
                problems.Add($"{name}: {window}");
            }

            problems.AddRange(overlay.Removes
                .Where(gone => !room.Props.Any(prop => Same(prop, gone)))
                .Select(gone => $"{name}: takes away a {gone.Kind} that the room has not at ({gone.Spot.X}, {gone.Spot.Z})"));
            problems.AddRange(PropPlacement.Problems(Lay(room, [overlay])).Where(problem => !own.Contains(problem))
                .Select(problem => $"{name}: {problem}"));
        }

        var all = overlays.Where(overlay => overlay.RoomId == room.Id).ToArray();
        if (all.Length > 1)
        {
            problems.AddRange(PropPlacement.Problems(Lay(room, all)).Where(problem => !own.Contains(problem))
                .Select(problem => $"{room.Id}, with all its overlays on: {problem}"));
        }

        return [.. problems.Distinct()];
    }

    private static RoomDefinition Lay(RoomDefinition room, IReadOnlyList<RoomOverlay> overlays)
    {
        var props = room.Props.Where(prop => !overlays.Any(overlay => overlay.Removes.Any(gone => Same(prop, gone)))).ToList();
        foreach (var overlay in overlays)
        {
            props.AddRange(overlay.Adds);
        }

        return room with { Props = props, Look = overlays.Select(overlay => overlay.Look).LastOrDefault(look => look is not null) ?? room.Look };
    }

    private static bool Same(Prop a, Prop b) => a.Kind == b.Kind && a.Spot == b.Spot;
}
