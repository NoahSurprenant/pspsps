using Pspsps.Domain.Moderation;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Moderation;

/// <summary>A player's ban and mute in force when read (null: none).</summary>
public sealed record Standing(Sanction? Ban, Sanction? Mute)
{
    public static Standing None { get; } = new(null, null);

    public static Standing Of(PlayerModeration? moderation, DateTimeOffset now) =>
        moderation is null ? None : new(moderation.BanAt(now), moderation.MuteAt(now));
}

/// <summary>A player banned or muted now, for the admins: who, and each sanction with the admin who decided it.</summary>
public sealed record SanctionedPlayer(
    Guid PlayerId, string DisplayName, Sanction? Ban, string? BannedByName, Sanction? Mute, string? MutedByName);

/// <summary>One line of the audit trail, with names: who did what to whom, when, why, until when.</summary>
public sealed record ModerationLogLine(
    Guid PlayerId, string PlayerName, ModerationKind Kind, Guid ByPlayerId, string ByName, DateTimeOffset At,
    string? Reason, DateTimeOffset? Until, string? Address = null);

/// <summary>
/// What the admin page shows: the open reports (oldest first), who is banned or muted now, what admins did lately, and the
/// networks banned now.
/// </summary>
public sealed record ModerationOverview(
    IReadOnlyList<OpenReport> Reports, IReadOnlyList<SanctionedPlayer> Sanctioned, IReadOnlyList<ModerationLogLine> Recent,
    IReadOnlyList<AddressBanLine> AddressBans);

/// <summary>A warning the player hasn't seen yet: its audit line's ID (to say it was seen), and its words.</summary>
public sealed record PendingWarning(Guid Id, string Text, DateTimeOffset At);

/// <summary>
/// An open report, for the admins: who reported whom (their names: the reporter's now, the reported cat's then), why, where,
/// when, and the room's last chat lines as the server had them, oldest first.
/// </summary>
public sealed record OpenReport(
    Guid Id, Guid ReporterId, string ReporterName, Guid ReportedId, string ReportedName, ReportReason Reason, string? Note,
    string WorldId, string Room, DateTimeOffset FiledAt, IReadOnlyList<ReportedLine> Context);

/// <summary>A report decided: about whom, what was done to them (null: dismissed), and how they stand after.</summary>
public sealed record ReportDecided(Guid ReportedId, ModerationAction? Action, Standing Standing);
