namespace Pspsps.Domain.Moderation;

/// <summary>
/// An admin, acting on another player: the only way to moderate anyone. Admins are listed by player ID in the server's
/// configuration (there is no other way to become one), so a moderator is made from that list (<see cref="Of"/>), which
/// refuses anyone not on it.
/// </summary>
public sealed class Moderator
{
    /// <summary>What anyone but an admin is told.</summary>
    public const string OnlyAdmins = "Only admins can moderate.";

    private readonly IReadOnlySet<Guid> _admins;

    private Moderator(Guid playerId, IReadOnlySet<Guid> admins)
    {
        PlayerId = playerId;
        _admins = admins;
    }

    /// <summary>The admin's player ID.</summary>
    public Guid PlayerId { get; }

    /// <summary>The player <paramref name="playerId"/> as a moderator, if they are one of <paramref name="admins"/>.</summary>
    public static Moderator Of(Guid playerId, IReadOnlySet<Guid> admins)
    {
        ArgumentNullException.ThrowIfNull(admins);
        return playerId != Guid.Empty && admins.Contains(playerId)
            ? new Moderator(playerId, admins)
            : throw new DomainException(OnlyAdmins);
    }

    /// <summary>Whether <paramref name="playerId"/> is an admin too (admins aren't moderated: the list changes them).</summary>
    public bool IsAdmin(Guid playerId) => _admins.Contains(playerId);
}
