namespace Pspsps.Application.Safety;

/// <summary>
/// A report filed: whether the reporter has the reported player muted now, whether the report muted them (not when they
/// were muted already, or every mute was used), and the reporter's mutes after.
/// </summary>
public sealed record ReportOutcome(bool Muted, bool MuteAdded, IReadOnlyList<MutedPlayer> Mutes);
