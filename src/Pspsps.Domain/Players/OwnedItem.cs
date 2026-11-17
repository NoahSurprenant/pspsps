namespace Pspsps.Domain.Players;

/// <summary>Something from the shop a player has (bought, or the coat they were born with). Part of the <see cref="Player"/> aggregate.</summary>
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
    }

    /// <summary>The shop item's ID (<see cref="Shop.ShopCatalog"/>).</summary>
    public string ItemId { get; private set; }

    /// <summary>When the player got it.</summary>
    public DateTimeOffset At { get; private set; }
}
