namespace Pspsps.Domain.Rooms;

/// <summary>
/// Where an outdoor room sits on the world grid: its south-west corner, in whole cells of <see cref="Cell"/> metres, so
/// rooms placed edge to edge line up by construction. A room with no place is an indoor room, reached only by doors.
/// </summary>
public sealed record WorldPlace(int X, int Z)
{
    public const double Cell = 2;

    /// <summary>Whether <paramref name="metres"/> is a whole number of cells.</summary>
    public static bool IsWholeCells(double metres) =>
        double.IsFinite(metres) && Math.Abs(metres / Cell - Math.Round(metres / Cell)) < 1e-9;

    /// <summary>Whether two rooms of these sizes, at these places, share floor (touching edges do not count).</summary>
    public static bool Overlap(WorldPlace a, RoomLayout sizeA, WorldPlace b, RoomLayout sizeB) =>
        a.X * Cell < b.X * Cell + sizeB.Width && b.X * Cell < a.X * Cell + sizeA.Width
        && a.Z * Cell < b.Z * Cell + sizeB.Depth && b.Z * Cell < a.Z * Cell + sizeA.Depth;
}
