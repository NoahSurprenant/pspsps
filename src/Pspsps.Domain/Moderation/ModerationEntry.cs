namespace Pspsps.Domain.Moderation;

/// <summary>
/// One line of a player's audit trail: who (<see cref="ByPlayerId"/>) did what (<see cref="Kind"/>) to them, when, why,
/// and until when. Made only by <see cref="PlayerModeration"/>, never changed.
/// </summary>
public sealed class ModerationEntry
{
    private ModerationEntry()
    {
    }

    internal ModerationEntry(
        Guid playerId, ModerationKind kind, Guid byPlayerId, DateTimeOffset at, string? reason, DateTimeOffset? until)
    {
        Id = Guid.CreateVersion7(at);
        PlayerId = playerId;
        Kind = kind;
        ByPlayerId = byPlayerId;
        At = at;
        Reason = reason;
        Until = until;
    }

    public Guid Id { get; private set; }

    /// <summary>The player it was done to.</summary>
    public Guid PlayerId { get; private set; }

    public ModerationKind Kind { get; private set; }

    /// <summary>The admin who did it.</summary>
    public Guid ByPlayerId { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Why (a warning's words); null for a lift.</summary>
    public string? Reason { get; private set; }

    /// <summary>When a mute or ban ends; null for good, and for the rest.</summary>
    public DateTimeOffset? Until { get; private set; }
}
