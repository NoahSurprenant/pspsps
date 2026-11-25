namespace Pspsps.Domain.Rooms;

/// <summary>
/// A raised stretch of a room's ground, <see cref="Height"/> metres up: its middle (<see cref="X"/>, <see cref="Z"/>) in
/// metres from the room's middle and its size. Cats walk up onto it by the steps round its edge, so it changes how the
/// room looks and what height a cat stands at, never where it may walk.
/// </summary>
public sealed record RoomTerrace(double X, double Z, double Width, double Depth, double Height)
{
    public const double MaxHeight = 3;

    public const double MinSize = 2;

    /// <summary>What is wrong with the terraces of <paramref name="room"/>: none for a room with none.</summary>
    public static IEnumerable<string> Problems(RoomDefinition room)
    {
        ArgumentNullException.ThrowIfNull(room);
        for (var index = 0; index < room.Terraces.Count; index++)
        {
            var terrace = room.Terraces[index];
            var name = $"{room.Id}/terrace {index + 1}";
            if (new[] { terrace.X, terrace.Z, terrace.Width, terrace.Depth, terrace.Height }.Any(value => !double.IsFinite(value)))
            {
                yield return $"{name}: has no size.";
            }
            else if (terrace.Width < MinSize || terrace.Depth < MinSize)
            {
                yield return $"{name}: is under {MinSize} m across.";
            }
            else if (terrace.Height <= 0 || terrace.Height > MaxHeight)
            {
                yield return $"{name}: is not between 0 and {MaxHeight} m high.";
            }
            else if (Math.Abs(terrace.X) + (terrace.Width / 2) > (room.Width / 2) + 1e-6 || Math.Abs(terrace.Z) + (terrace.Depth / 2) > (room.Depth / 2) + 1e-6)
            {
                yield return $"{name}: reaches past the room's floor.";
            }
            else if (room.Place is null)
            {
                yield return $"{name}: is in a room with no place on the world map.";
            }
        }
    }
}
