namespace Pspsps.Domain.Fishing;

public static class FishCatalog
{
    public static IReadOnlyList<Fish> All { get; } =
    [
        new("boot", "Old boot", Coins: 1, Weight: 10),
        new("minnow", "Little minnow", Coins: 5, Weight: 35),
        new("goldfish", "Goldfish", Coins: 10, Weight: 28),
        new("trout", "Rainbow trout", Coins: 20, Weight: 16),
        new("catfish", "Big catfish", Coins: 40, Weight: 8),
        new("koi", "Golden koi", Coins: 100, Weight: 3),
    ];

    /// <summary>A fish by weight: <paramref name="roll"/> is in [0, 1).</summary>
    public static Fish Pick(double roll)
    {
        if (roll is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "A roll is in [0, 1).");
        }

        var total = All.Sum(f => f.Weight);
        var point = roll * total;
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
