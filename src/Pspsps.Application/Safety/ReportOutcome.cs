namespace Pspsps.Application.Safety;

/// <summary>A report filed: whether the reporter has the reported player muted now, and the reporter's mutes after.</summary>
public sealed record ReportOutcome(bool Muted, IReadOnlyList<MutedPlayer> Mutes);
