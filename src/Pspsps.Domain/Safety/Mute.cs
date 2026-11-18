namespace Pspsps.Domain.Safety;

/// <summary>
/// One player muting another: the muter stops seeing the muted player's chat (their browser hides it), and nobody else
/// is affected. A player's own choice, with no review. An aggregate of its own, keyed by the pair (muter, muted), so a
/// pair has at most one, also when two tabs mute at once. Unmuting deletes it.
/// </summary>
public sealed class Mute
{
    /// <summary>How many players one player can have muted.</summary>
    public const int MaxMutes = 500;

    private Mute()
    {
    }

    private Mute(Guid muterId, Guid mutedId, DateTimeOffset now)
    {
        MuterId = muterId;
        MutedId = mutedId;
        MutedAt = now;
    }

    /// <summary>Who muted.</summary>
    public Guid MuterId { get; private set; }

    /// <summary>Who is muted.</summary>
    public Guid MutedId { get; private set; }

    public DateTimeOffset MutedAt { get; private set; }

    /// <summary>
    /// <paramref name="muterId"/> mutes <paramref name="mutedId"/>, with the mutes they have already
    /// (<paramref name="soFar"/>): not themself, not twice, and at most <see cref="MaxMutes"/>. Mutes at the same moment
    /// can pass the limit by one or two, which is harmless.
    /// </summary>
    public static Mute Create(Guid muterId, Guid mutedId, MutesSoFar soFar, DateTimeOffset now)
    {
        if (muterId == Guid.Empty || mutedId == Guid.Empty)
        {
            throw new DomainException("That cat doesn't exist.");
        }

        if (muterId == mutedId)
        {
            throw new DomainException("You can't mute yourself.");
        }

        if (soFar.IncludesThem)
        {
            throw new DomainException("You've muted that cat already.");
        }

        if (soFar.Count >= MaxMutes)
        {
            throw new DomainException($"You can mute at most {MaxMutes} cats. Unmute some first.");
        }

        return new Mute(muterId, mutedId, now);
    }

    /// <summary>Checks that <paramref name="playerId"/> may unmute: only the one who muted. The caller deletes it.</summary>
    public void Unmute(Guid playerId)
    {
        if (playerId != MuterId)
        {
            throw new DomainException("That isn't your mute.");
        }
    }
}
