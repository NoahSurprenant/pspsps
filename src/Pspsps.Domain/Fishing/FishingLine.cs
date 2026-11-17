namespace Pspsps.Domain.Fishing;

public enum ReelOutcome
{
    /// <summary>Reeled at the bite: <see cref="ReelResult.Fish"/> is the catch.</summary>
    Caught,
    /// <summary>Reeled before anything bit: the fish is scared off.</summary>
    TooEarly,
    /// <summary>Reeled after the bite was over: it got away.</summary>
    TooLate,
}

/// <summary>How a reel went, and what it pays: a catch pays its fish's coins (even none), anything else nothing.</summary>
public sealed record ReelResult(ReelOutcome Outcome, Fish? Fish)
{
    public bool IsCatch => Outcome == ReelOutcome.Caught;

    public int Coins => Fish?.Coins ?? 0;
}

/// <summary>
/// One cat's line in the water. The bite comes at a time only the server knows (<see cref="BiteAt"/>); the cat has
/// <see cref="BiteWindow"/> to reel it in from when it is told (<see cref="BitAt"/>), however late that is. Lives in
/// memory in the room, like the cat.
/// </summary>
public sealed class FishingLine
{
    public static readonly TimeSpan ShortestWait = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan BiteWindow = TimeSpan.FromSeconds(1.5);

    private FishingLine(DateTimeOffset biteAt)
    {
        BiteAt = biteAt;
    }

    /// <summary>When the fish is due to bite: when the room should tell the cat.</summary>
    public DateTimeOffset BiteAt { get; }

    /// <summary>When the cat was told of the bite, or null while it hasn't been yet.</summary>
    public DateTimeOffset? BitAt { get; private set; }

    /// <summary>Casts at <paramref name="now"/>; <paramref name="roll"/> in [0, 1) picks the wait for the bite.</summary>
    public static FishingLine Cast(DateTimeOffset now, double roll)
    {
        if (roll is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "A roll is in [0, 1).");
        }

        return new FishingLine(now + ShortestWait + ((LongestWait - ShortestWait) * roll));
    }

    /// <summary>
    /// The fish bites, and the cat is told, at <paramref name="now"/> (the room's timer can fire later than
    /// <see cref="BiteAt"/>): the window to reel runs from here. A line bites once.
    /// </summary>
    public void Bite(DateTimeOffset now) => BitAt ??= now;

    /// <summary>Reels in at <paramref name="now"/>; <paramref name="roll"/> in [0, 1) picks the fish when caught.</summary>
    public ReelResult Reel(DateTimeOffset now, double roll)
    {
        if (BitAt is not { } bit || now < bit)
        {
            return new ReelResult(ReelOutcome.TooEarly, null);
        }

        if (now > bit + BiteWindow)
        {
            return new ReelResult(ReelOutcome.TooLate, null);
        }

        return new ReelResult(ReelOutcome.Caught, FishCatalog.Pick(roll));
    }
}
