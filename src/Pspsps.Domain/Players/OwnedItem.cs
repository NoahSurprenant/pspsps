namespace Pspsps.Domain.Players;

/// <summary>
/// Something from the shop a player has (bought, or what they were born with), and how many: one of anything a cat
/// wears, up to <see cref="Shop.ShopCatalog.MaxOfEachFurniture"/> of a piece of furniture. Part of the
/// <see cref="Player"/> aggregate.
/// </summary>
public sealed class OwnedItem
{
    private OwnedItem()
    {
        ItemId = null!;
    }

    internal OwnedItem(string itemId, DateTimeOffset at)
    {
        ItemId = itemId;
        At = at;
        Count = 1;
    }

    /// <summary>The shop item's ID (<see cref="Shop.ShopCatalog"/>).</summary>
    public string ItemId { get; private set; }

    /// <summary>When the player got their first.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>How many the player has: always 1 for what a cat wears.</summary>
    public int Count { get; private set; }

    internal void AddOne() => Count++;
}
