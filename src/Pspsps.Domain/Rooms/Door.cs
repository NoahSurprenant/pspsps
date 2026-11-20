namespace Pspsps.Domain.Rooms;

/// <summary>
/// A doorway out of a room: a round spot on its floor (<see cref="Spot"/>, <see cref="Radius"/>) that a cat walks onto
/// to go through, to the door <see cref="ToDoor"/> of the room <see cref="ToRoom"/>, or, with no door there (a home's
/// door out, which isn't one of the room's), to that room's spawn point.
/// </summary>
public sealed record Door(string Id, Position Spot, double Radius, string ToRoom, string? ToDoor)
{
    /// <summary>How far past the door's edge, towards the room's middle, a cat coming in through it stands.</summary>
    public const double StepIn = 0.75;

    /// <summary>The floor edge this exit leads through, when the room is on the world map.</summary>
    public RoomEdge? Edge { get; init; }

    /// <summary>Whether a cat standing at <paramref name="cat"/> is on the door, and can go through it.</summary>
    public bool InReach(Position cat) => cat.DistanceTo(Spot) <= Radius;

    /// <summary>
    /// Where a cat coming in through this door stands: just inside it, towards the room's middle (the floor is centred on
    /// the origin), off the door so it doesn't walk straight back out.
    /// </summary>
    public Position Arrival
    {
        get
        {
            var inward = Radius + StepIn;
            if (Edge is { } edge)
            {
                // Keep the cat on the same line as the passage across the world boundary.
                return edge switch
                {
                    RoomEdge.North => Spot with { Z = Spot.Z - inward },
                    RoomEdge.South => Spot with { Z = Spot.Z + inward },
                    RoomEdge.East => Spot with { X = Spot.X - inward },
                    RoomEdge.West => Spot with { X = Spot.X + inward },
                    _ => throw new InvalidOperationException("Unknown entrance edge."),
                };
            }

            var length = Spot.DistanceTo(new Position(0, 0));
            if (length == 0)
            {
                // A door in the middle of the floor has no "inwards": step towards the front.
                return Spot with { Z = Spot.Z - Radius - StepIn };
            }

            var step = inward / length;
            return new Position(Spot.X - (Spot.X * step), Spot.Z - (Spot.Z * step));
        }
    }
}
