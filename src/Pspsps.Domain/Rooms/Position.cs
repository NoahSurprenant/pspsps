namespace Pspsps.Domain.Rooms;

/// <summary>A spot on a room's floor, in metres from the room's centre.</summary>
public readonly record struct Position(double X, double Z)
{
    /// <summary>How far it is, straight across the floor, to <paramref name="other"/>.</summary>
    public double DistanceTo(Position other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
