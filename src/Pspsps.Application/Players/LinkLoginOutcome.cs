namespace Pspsps.Application.Players;

/// <summary>What linking an external login to a player did. The client has the words for each.</summary>
public enum LinkLoginOutcome
{
    /// <summary>The login is now the player's (or already was): they can sign in with it from anywhere.</summary>
    Linked,

    /// <summary>The login belongs to another cat. Nothing changed: the player chooses, nothing is merged.</summary>
    TakenByAnotherPlayer,

    /// <summary>The cat already has a login from that provider (one per provider).</summary>
    Refused,

    /// <summary>The signed-in player no longer exists.</summary>
    PlayerNotFound,

    /// <summary>The login itself isn't valid (no provider user ID, an unknown provider).</summary>
    InvalidLogin,

    /// <summary>The cat kept changing while it was linked (coins earned at every try): nothing changed; try again.</summary>
    Busy,
}
