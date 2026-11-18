namespace Pspsps.Domain.Safety;

/// <summary>Where a report stands. Every report starts open; reviewing them (the moderation tools) adds what comes next.</summary>
public enum ReportStatus
{
    /// <summary>Filed, not reviewed yet.</summary>
    Open,
}
