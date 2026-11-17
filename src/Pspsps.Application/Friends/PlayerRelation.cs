using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Friends;

/// <summary>
/// Another player as one player sees them: who they are, what their cat wears (shop item IDs), and where the two stand.
/// </summary>
public sealed record PlayerRelation(
    Guid PlayerId, string DisplayName, FurColor FurColor, FriendState State, string? Hat = null, string? Collar = null);
