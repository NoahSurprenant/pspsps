namespace Pspsps.Domain.Rooms;

/// <summary>A round pond on a room's floor. Cats fish from its edge.</summary>
public sealed record Pond(Position Center, double Radius)
{
    /// <summary>How far from the water a cat can stand and still reach it with a line.</summary>
    public const double Reach = 1.5;

    public bool InReach(Position cat)
    {
        var dx = cat.X - Center.X;
        var dz = cat.Z - Center.Z;
        return Math.Sqrt((dx * dx) + (dz * dz)) <= Radius + Reach;
    }
}
