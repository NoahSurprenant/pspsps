using System.Globalization;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// Where a room's props may stand. Props are decoration that cats walk through, but they stay out of the way of what a
/// player walks to: on the floor, off the doors and the spots just inside them, off the pond and the ring round it a cat
/// fishes from, off the spawn point, off the straight walks between all of these (a cat walks straight to where it is
/// sent), and off each other. The same rules hold for the rooms' own props and for the furniture in players' homes
/// (<see cref="Homes.Home"/>).
/// </summary>
public static class PropPlacement
{
    /// <summary>The room a cat takes up round where it stands, and half the width of a walk.</summary>
    public const double CatRoom = 0.6;

    /// <summary>
    /// What is wrong with where <paramref name="room"/>'s props stand, one line per problem; empty when they all keep
    /// clear.
    /// </summary>
    public static IReadOnlyList<string> Problems(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var problems = new List<string>();
        var check = CheckerFor(room);
        for (var i = 0; i < room.Props.Count; i++)
        {
            var prop = room.Props[i];
            var name = $"{room.Id}: the {prop.Kind} at {At(prop.Spot)}";
            // Each pair once: a prop is checked against the props after it.
            problems.AddRange(check(prop, room.Props.Skip(i + 1)).Select(problem => $"{name} {problem.Says}"));
        }

        return problems;
    }

    /// <summary>
    /// What is wrong with <paramref name="prop"/> standing where it does in <paramref name="room"/> (its doors, pond and
    /// spawn point), among <paramref name="others"/>; empty when it keeps clear.
    /// </summary>
    public static IReadOnlyList<PropProblem> ProblemsOf(RoomDefinition room, Prop prop, IEnumerable<Prop> others) =>
        CheckerFor(room)(prop, others);

    /// <summary>
    /// <see cref="ProblemsOf(RoomDefinition, Prop, IEnumerable{Prop})"/> for one room, its doors, pond and spawn point worked out once: for checking many props, or
    /// one prop at many spots.
    /// </summary>
    public static Func<Prop, IEnumerable<Prop>, IReadOnlyList<PropProblem>> CheckerFor(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var stops = Stops(room);
        return (prop, others) => ProblemsOf(room, stops, prop, others);
    }

    private static List<PropProblem> ProblemsOf(RoomDefinition room, List<Stop> stops, Prop prop, IEnumerable<Prop> others)
    {
        ArgumentNullException.ThrowIfNull(prop);
        ArgumentNullException.ThrowIfNull(others);
        var problems = new List<PropProblem>();
        if (!PropKinds.All.Contains(prop.Kind))
        {
            problems.Add(new(PropProblemKind.UnknownKind, "is no kind of prop the browser draws"));
        }

        if (!double.IsFinite(prop.Spot.X) || !double.IsFinite(prop.Spot.Z) || !double.IsFinite(prop.Radius))
        {
            problems.Add(new(PropProblemKind.OffFloor, "is nowhere on the floor"));
            return problems;
        }

        if (prop.Radius <= 0)
        {
            problems.Add(new(PropProblemKind.NoSize, "has no size"));
        }

        if (Math.Abs(prop.Spot.X) + prop.Radius > room.Width / 2 || Math.Abs(prop.Spot.Z) + prop.Radius > room.Depth / 2)
        {
            problems.Add(new(PropProblemKind.OffFloor, "is partly off the floor"));
        }

        foreach (var stop in stops)
        {
            if (prop.Spot.DistanceTo(stop.Spot) < prop.Radius + stop.Clear)
            {
                problems.Add(new(PropProblemKind.InTheWay, stop.On));
            }
        }

        for (var a = 0; a < stops.Count; a++)
        {
            for (var b = a + 1; b < stops.Count; b++)
            {
                if (DistanceToWalk(prop.Spot, stops[a].Spot, stops[b].Spot) < prop.Radius + CatRoom)
                {
                    problems.Add(new(PropProblemKind.InTheWay, $"is on the walk from {stops[a].Label} to {stops[b].Label}"));
                }
            }
        }

        foreach (var other in others)
        {
            if (prop.Spot.DistanceTo(other.Spot) < prop.Radius + other.Radius)
            {
                problems.Add(new(PropProblemKind.Overlaps, $"overlaps the {other.Kind} at {At(other.Spot)}"));
            }
        }

        return problems;
    }

    /// <summary>
    /// A place cats walk to (its <c>Label</c> in a walk's name), how far from its middle a prop must keep beyond its own
    /// size (<c>Clear</c>), and what a prop nearer is (<c>On</c>).
    /// </summary>
    private readonly record struct Stop(string Label, Position Spot, double Clear, string On);

    /// <summary>The places cats walk between: the spawn point, each door and the spot just inside it, and the pond.</summary>
    private static List<Stop> Stops(RoomDefinition room)
    {
        var stops = new List<Stop> { new("the spawn point", room.Spawn, CatRoom, "is on the spawn point") };
        foreach (var door in room.Doors)
        {
            stops.Add(new($"the door {door.Id}", door.Spot, door.Radius + CatRoom, $"is on the door {door.Id}"));
            var wayIn = $"the way in by the door {door.Id}";
            stops.Add(new(wayIn, door.Arrival, CatRoom, $"is on {wayIn}"));
        }

        if (room.Pond is { } pond)
        {
            stops.Add(new("the pond", pond.Center, pond.Radius + Pond.Reach, "is in the pond or where cats fish from"));
        }

        return stops;
    }

    /// <summary>How far <paramref name="spot"/> is from the straight walk between <paramref name="from"/> and <paramref name="to"/>.</summary>
    private static double DistanceToWalk(Position spot, Position from, Position to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        var lengthSquared = (dx * dx) + (dz * dz);
        var along = lengthSquared == 0
            ? 0
            : Math.Clamp((((spot.X - from.X) * dx) + ((spot.Z - from.Z) * dz)) / lengthSquared, 0, 1);
        return spot.DistanceTo(new Position(from.X + (along * dx), from.Z + (along * dz)));
    }

    private static string At(Position spot) => string.Create(CultureInfo.InvariantCulture, $"({spot.X}, {spot.Z})");
}

/// <summary>What kind of problem a prop's place is.</summary>
public enum PropProblemKind
{
    /// <summary>The browser doesn't draw that kind.</summary>
    UnknownKind,

    /// <summary>Its circle has no size.</summary>
    NoSize,

    /// <summary>Partly or wholly off the floor.</summary>
    OffFloor,

    /// <summary>On a door, the way in by one, the pond, the spawn point, or a walk between them.</summary>
    InTheWay,

    /// <summary>On another prop.</summary>
    Overlaps,
}

/// <summary>One problem with where a prop stands: its kind, and what it is, said after the prop's name.</summary>
public sealed record PropProblem(PropProblemKind Kind, string Says);
