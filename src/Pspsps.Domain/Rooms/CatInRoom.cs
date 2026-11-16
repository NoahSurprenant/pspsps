using Pspsps.Domain.Players;

namespace Pspsps.Domain.Rooms;

/// <summary>A cat standing in a room right now.</summary>
public sealed record CatInRoom(Guid PlayerId, string DisplayName, FurColor FurColor, Position Position);
