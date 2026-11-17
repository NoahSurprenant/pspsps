namespace Pspsps.Domain.Rooms;

/// <summary>
/// A piece of a room's decoration: what it is (<see cref="Kind"/>, one of <see cref="PropKinds"/>, which the browser
/// draws), where it stands (<see cref="Spot"/>), the circle round that spot it is drawn inside (<see cref="Radius"/>),
/// and which way it faces (<see cref="Facing"/>, in radians about the vertical: 0 faces the front of the room, where
/// the camera is). Decoration only: cats walk through props.
/// </summary>
public sealed record Prop(string Kind, Position Spot, double Radius, double Facing = 0);

/// <summary>The kinds of prop the browser knows how to draw.</summary>
public static class PropKinds
{
    public const string Tree = "tree";
    public const string Bench = "bench";
    public const string Lamp = "lamp";
    public const string FlowerBed = "flower-bed";
    public const string CafeTable = "cafe-table";
    public const string Counter = "counter";
    public const string Plant = "plant";
    public const string CatTree = "cat-tree";

    public static IReadOnlyList<string> All { get; } = [Tree, Bench, Lamp, FlowerBed, CafeTable, Counter, Plant, CatTree];
}
