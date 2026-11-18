namespace Pspsps.Domain.Moderation;

/// <summary>
/// Where a player stands with the admins: a ban (until a time, or for good), a mute in every room (until a time), a
/// warning they haven't seen yet, and the trail of what was done to them. An aggregate of its own, keyed by the player,
/// made the first time an admin acts on them. Only an admin acts (a <see cref="Moderator"/>), never on themselves nor
/// on another admin, and always says why: the player is shown it. A ban or mute ends at its time by itself, or when an
/// admin lifts it; a new one takes the place of the last.
/// </summary>
public sealed class PlayerModeration
{
    /// <summary>The longest reason (or warning) kept.</summary>
    public const int MaxReasonLength = 200;

    private readonly List<ModerationEntry> _entries = [];

    private PlayerModeration()
    {
    }

    private PlayerModeration(Guid playerId)
    {
        PlayerId = playerId;
    }

    public Guid PlayerId { get; private set; }

    public string? BanReason { get; private set; }

    public DateTimeOffset? BannedAt { get; private set; }

    /// <summary>When the ban ends; null with a <see cref="BanReason"/> is for good.</summary>
    public DateTimeOffset? BannedUntil { get; private set; }

    public Guid? BannedBy { get; private set; }

    public string? MuteReason { get; private set; }

    public DateTimeOffset? MutedAt { get; private set; }

    public DateTimeOffset? MutedUntil { get; private set; }

    public Guid? MutedBy { get; private set; }

    /// <summary>A warning the player hasn't been shown yet; shown once (<see cref="TakeWarning"/>).</summary>
    public string? Warning { get; private set; }

    /// <summary>Counts every change: two admins acting at once are decided one after the other.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// The audit trail: who did what, when and why. Only the lines added since it was read, unless it was read with them.
    /// </summary>
    public IReadOnlyCollection<ModerationEntry> Entries => _entries.AsReadOnly();

    /// <summary>The last ban, whether or not it has ended.</summary>
    public Sanction? Ban => BanReason is { } reason && BannedAt is { } at && BannedBy is { } by
        ? new Sanction(reason, at, BannedUntil, by)
        : null;

    /// <summary>The last mute, whether or not it has ended.</summary>
    public Sanction? Mute => MuteReason is { } reason && MutedAt is { } at && MutedBy is { } by
        ? new Sanction(reason, at, MutedUntil, by)
        : null;

    /// <summary>A player no admin has acted on yet.</summary>
    public static PlayerModeration For(Guid playerId) =>
        playerId == Guid.Empty ? throw new DomainException("That cat doesn't exist.") : new PlayerModeration(playerId);

    /// <summary>The ban in force at <paramref name="now"/>, if any.</summary>
    public Sanction? BanAt(DateTimeOffset now) => Ban is { } ban && ban.InForceAt(now) ? ban : null;

    /// <summary>The mute in force at <paramref name="now"/>, if any.</summary>
    public Sanction? MuteAt(DateTimeOffset now) => Mute is { } mute && mute.InForceAt(now) ? mute : null;

    /// <summary>Warns the player: <paramref name="message"/> is shown to them once, the next time they play or at once.</summary>
    public void Warn(Moderator by, string message, DateTimeOffset now)
    {
        EnsureMayModerate(by);
        var text = ReasonOf(message);
        Warning = text;
        Record(ModerationKind.Warned, by, now, text, until: null);
    }

    /// <summary>Mutes the player in every room until <paramref name="until"/>: they can't chat, and are told why.</summary>
    public void MuteUntil(Moderator by, string reason, DateTimeOffset until, DateTimeOffset now)
    {
        EnsureMayModerate(by);
        var text = ReasonOf(reason);
        if (until <= now)
        {
            throw new DomainException("A mute ends later than now.");
        }

        (MuteReason, MutedAt, MutedUntil, MutedBy) = (text, now, until, by.PlayerId);
        Record(ModerationKind.Muted, by, now, text, until);
    }

    /// <summary>
    /// Bans the player until <paramref name="until"/>, or for good when it's null: they can't play, and are told why and
    /// until when.
    /// </summary>
    public void BanUntil(Moderator by, string reason, DateTimeOffset? until, DateTimeOffset now)
    {
        EnsureMayModerate(by);
        var text = ReasonOf(reason);
        if (until <= now)
        {
            throw new DomainException("A ban ends later than now.");
        }

        (BanReason, BannedAt, BannedUntil, BannedBy) = (text, now, until, by.PlayerId);
        Record(ModerationKind.Banned, by, now, text, until);
    }

    /// <summary>Lifts the ban and the mute in force, whichever there are; refused when there is neither.</summary>
    public void Lift(Moderator by, DateTimeOffset now)
    {
        EnsureMayModerate(by);
        var banned = BanAt(now) is not null;
        var muted = MuteAt(now) is not null;
        if (!banned && !muted)
        {
            throw new DomainException("That cat isn't banned or muted.");
        }

        if (banned)
        {
            (BanReason, BannedAt, BannedUntil, BannedBy) = (null, null, null, null);
            Record(ModerationKind.BanLifted, by, now, reason: null, until: null);
        }

        if (muted)
        {
            (MuteReason, MutedAt, MutedUntil, MutedBy) = (null, null, null, null);
            Record(ModerationKind.MuteLifted, by, now, reason: null, until: null);
        }
    }

    /// <summary>Refuses chat while the player is muted (at <paramref name="now"/>), saying why and until when.</summary>
    public void EnsureMayChat(DateTimeOffset now) => EnsureMayChat(Mute, now);

    /// <summary>
    /// Refuses chat under <paramref name="mute"/> (a player's last mute, as read) while it holds at <paramref name="now"/>.
    /// </summary>
    public static void EnsureMayChat(Sanction? mute, DateTimeOffset now)
    {
        if (mute is not null && mute.InForceAt(now))
        {
            throw new DomainException(mute.SaysMuted());
        }
    }

    /// <summary>The warning not shown yet, if any, which counts as shown from now on.</summary>
    public string? TakeWarning()
    {
        var warning = Warning;
        if (warning is not null)
        {
            Warning = null;
            Version++;
        }

        return warning;
    }

    /// <summary>A reason (or a warning), as kept: trimmed, said, and short enough to show.</summary>
    public static string ReasonOf(string? reason)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new DomainException("Say why: the player is shown it.");
        }

        if (text.Length > MaxReasonLength)
        {
            throw new DomainException($"Keep the reason to {MaxReasonLength} characters.");
        }

        return text;
    }

    private void EnsureMayModerate(Moderator by)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (by.PlayerId == PlayerId)
        {
            throw new DomainException("You can't moderate yourself.");
        }

        if (by.IsAdmin(PlayerId))
        {
            throw new DomainException("That cat is an admin. Admins are changed in the server's settings, not moderated.");
        }
    }

    private void Record(ModerationKind kind, Moderator by, DateTimeOffset now, string? reason, DateTimeOffset? until)
    {
        _entries.Add(new ModerationEntry(PlayerId, kind, by.PlayerId, now, reason, until));
        Version++;
    }
}
