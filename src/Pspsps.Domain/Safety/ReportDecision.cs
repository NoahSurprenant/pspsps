namespace Pspsps.Domain.Safety;

/// <summary>What an admin decided on a report.</summary>
public enum ReportDecision
{
    /// <summary>Nothing to do.</summary>
    Dismissed,

    /// <summary>The reported player was warned.</summary>
    Warned,

    /// <summary>The reported player was muted in every room.</summary>
    Muted,

    /// <summary>The reported player was banned.</summary>
    Banned,
}
