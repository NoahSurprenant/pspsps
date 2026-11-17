using Pspsps.Domain.Players;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// A cat in a room right now: where it stands, whether it's sitting or asleep there, and what it wears (its fur, and the
/// hat and collar it has on, as shop item IDs).
/// </summary>
public sealed record CatInRoom(
    Guid PlayerId,
    string DisplayName,
    FurColor FurColor,
    Position Position,
    CatPose Pose = CatPose.Standing,
    string? Hat = null,
    string? Collar = null);
