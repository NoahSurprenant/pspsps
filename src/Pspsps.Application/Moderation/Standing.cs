using Pspsps.Domain.Moderation;

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
    string? Reason, DateTimeOffset? Until);

/// <summary>What the admin page shows: who is banned or muted now, and what admins did lately.</summary>
public sealed record ModerationOverview(IReadOnlyList<SanctionedPlayer> Sanctioned, IReadOnlyList<ModerationLogLine> Recent);

/// <summary>A warning the player hasn't seen yet: its audit line's ID (to say it was seen), and its words.</summary>
public sealed record PendingWarning(Guid Id, string Text, DateTimeOffset At);
