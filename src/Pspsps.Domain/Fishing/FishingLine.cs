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

public sealed record ReelResult(ReelOutcome Outcome, Fish? Fish);

/// <summary>
/// One cat's line in the water. The bite comes at a time only the server knows; the cat has <see cref="BiteWindow"/>
/// from then to reel it in. Lives in memory in the room, like the cat.
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

    public DateTimeOffset BiteAt { get; }

    /// <summary>Casts at <paramref name="now"/>; <paramref name="roll"/> in [0, 1) picks the wait for the bite.</summary>
    public static FishingLine Cast(DateTimeOffset now, double roll)
    {
        if (roll is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "A roll is in [0, 1).");
        }

        return new FishingLine(now + ShortestWait + ((LongestWait - ShortestWait) * roll));
    }

    /// <summary>Reels in at <paramref name="now"/>; <paramref name="roll"/> in [0, 1) picks the fish when caught.</summary>
    public ReelResult Reel(DateTimeOffset now, double roll)
    {
        if (now < BiteAt)
        {
            return new ReelResult(ReelOutcome.TooEarly, null);
        }

        if (now > BiteAt + BiteWindow)
        {
            return new ReelResult(ReelOutcome.TooLate, null);
        }

        return new ReelResult(ReelOutcome.Caught, FishCatalog.Pick(roll));
    }
}
