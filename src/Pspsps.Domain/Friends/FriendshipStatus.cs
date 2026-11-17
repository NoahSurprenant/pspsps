namespace Pspsps.Domain.Friends;

/// <summary>Where a friendship stands. A declined or ended friendship isn't kept: it's deleted.</summary>
public enum FriendshipStatus
{
    /// <summary>Asked, not answered: the addressee accepts or declines it; the requester can take it back.</summary>
    Pending,

    /// <summary>Friends: either of them can end it.</summary>
    Accepted,
}
