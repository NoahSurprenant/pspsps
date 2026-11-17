namespace Pspsps.Domain.Friends;

/// <summary>Where one player stands with another, as the first sees it (a player card's button).</summary>
public enum FriendState
{
    /// <summary>Nothing between them: "Add friend".</summary>
    None,

    /// <summary>This player asked; the other hasn't answered.</summary>
    RequestSent,

    /// <summary>The other asked this player: accept or decline.</summary>
    RequestReceived,

    Friends,
}
