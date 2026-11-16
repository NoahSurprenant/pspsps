namespace Pspsps.Domain.Rooms;

/// <summary>A room's layout: a rectangular floor centred on the origin, where cats arrive, and how many fit.</summary>
public sealed record RoomDefinition(string Id, string Name, double Width, double Depth, Position Spawn, int Capacity)
{
    /// <summary>The nearest spot on the floor to <paramref name="target"/>.</summary>
    public Position Clamp(Position target) => new(
        Math.Clamp(target.X, -Width / 2, Width / 2),
        Math.Clamp(target.Z, -Depth / 2, Depth / 2));
}
