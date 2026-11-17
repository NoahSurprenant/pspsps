namespace Pspsps.Domain.Fishing;

public static class FishCatalog
{
    /// <summary>Every fish there is. Each pays at least one coin: a catch is always paid.</summary>
    public static IReadOnlyList<Fish> All { get; } =
    [
        new("boot", "Old boot", Coins: 1, Weight: 10),
        new("minnow", "Little minnow", Coins: 5, Weight: 35),
        new("goldfish", "Goldfish", Coins: 10, Weight: 28),
        new("trout", "Rainbow trout", Coins: 20, Weight: 16),
        new("catfish", "Big catfish", Coins: 40, Weight: 8),
        new("koi", "Golden koi", Coins: 100, Weight: 3),
    ];

    // After All: static initializers run in order.
    private static readonly int TotalWeight = All.Sum(f => f.Weight);

    /// <summary>A fish by weight: <paramref name="roll"/> is in [0, 1).</summary>
    public static Fish Pick(double roll)
    {
        Roll.Check(roll);
        var point = roll * TotalWeight;
        foreach (var fish in All)
        {
            if (point < fish.Weight)
            {
                return fish;
            }

            point -= fish.Weight;
        }

        return All[^1];
    }
}
