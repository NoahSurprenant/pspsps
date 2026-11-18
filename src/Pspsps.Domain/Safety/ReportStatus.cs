namespace Pspsps.Domain.Safety;

/// <summary>Where a report stands: open until an admin decides it (<see cref="ReportDecision"/>).</summary>
public enum ReportStatus
{
    /// <summary>Filed, not reviewed yet.</summary>
    Open,

    /// <summary>An admin decided it: dismissed it, or acted on the reported player.</summary>
    Closed,
}
