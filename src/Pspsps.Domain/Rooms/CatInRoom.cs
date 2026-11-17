using Pspsps.Domain.Players;

namespace Pspsps.Domain.Rooms;

/// <summary>A cat in a room right now: where it stands, and whether it's sitting or asleep there.</summary>
public sealed record CatInRoom(Guid PlayerId, string DisplayName, FurColor FurColor, Position Position, CatPose Pose = CatPose.Standing);
