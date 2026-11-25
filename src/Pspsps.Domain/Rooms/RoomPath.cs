namespace Pspsps.Domain.Rooms;

/// <summary>
/// A narrow strip of floor leaving a room's edge, <see cref="Length"/> metres out, <see cref="Width"/> wide and centred
/// <see cref="Offset"/> across the edge from its midpoint (as a door's offset is): the way cats walk to a room that is
/// not touching this one. Its far end meets another room's edge.
/// </summary>
public sealed record RoomPath(RoomEdge Edge, double Offset, double Width, double Length)
{
    /// <summary>The strip as a box on the world: its middle and size, for a room whose middle is (<paramref name="cx"/>, <paramref name="cz"/>).</summary>
    public (double X, double Z, double Width, double Depth) Floor(double cx, double cz, double roomWidth, double roomDepth) => Edge switch
    {
        RoomEdge.North => (cx + Offset, cz + (roomDepth / 2) + (Length / 2), Width, Length),
        RoomEdge.South => (cx + Offset, cz - (roomDepth / 2) - (Length / 2), Width, Length),
        RoomEdge.East => (cx + (roomWidth / 2) + (Length / 2), cz + Offset, Length, Width),
        RoomEdge.West => (cx - (roomWidth / 2) - (Length / 2), cz + Offset, Length, Width),
        _ => throw new ArgumentOutOfRangeException(nameof(roomWidth)),
    };
}
