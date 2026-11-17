namespace Pspsps.Domain.Homes;

/// <summary>Who may come into a player's home, besides the player: the owner chooses.</summary>
public enum HomeVisitors
{
    /// <summary>The owner's friends: every home starts so.</summary>
    Friends,

    /// <summary>Any cat.</summary>
    Anyone,

    /// <summary>No one but the owner.</summary>
    Nobody,
}
