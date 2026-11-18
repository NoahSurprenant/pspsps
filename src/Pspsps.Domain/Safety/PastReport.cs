namespace Pspsps.Domain.Safety;

/// <summary>A report a player filed before: whom it was about, when, and where it stands. What the limits count.</summary>
public readonly record struct PastReport(Guid ReportedId, DateTimeOffset FiledAt, ReportStatus Status);
