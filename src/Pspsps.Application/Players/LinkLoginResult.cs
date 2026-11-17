namespace Pspsps.Application.Players;

public enum LinkLoginOutcome
{
    /// <summary>The login is now the player's (or already was): they can sign in with it from anywhere.</summary>
    Linked,

    /// <summary>The login belongs to another cat. Nothing changed: the player chooses, nothing is merged.</summary>
    TakenByAnotherPlayer,

    /// <summary>The player's own rules refused it (a second login from the same provider, say); see the message.</summary>
    Refused,

    /// <summary>The signed-in player no longer exists.</summary>
    PlayerNotFound,
}

/// <summary>What linking an external login to a player did. <see cref="Message"/> is safe to show the player.</summary>
public sealed record LinkLoginResult(LinkLoginOutcome Outcome, PlayerProfile? Player, string? Message = null);
