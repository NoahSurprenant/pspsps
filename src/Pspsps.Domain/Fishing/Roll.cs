namespace Pspsps.Domain.Fishing;

/// <summary>A random roll, in [0, 1): what picks a bite time or a fish.</summary>
internal static class Roll
{
    public static void Check(double roll)
    {
        if (roll is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "A roll is in [0, 1).");
        }
    }
}
