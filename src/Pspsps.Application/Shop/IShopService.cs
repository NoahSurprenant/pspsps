namespace Pspsps.Application.Shop;

/// <summary>
/// Buying from the shop and dressing the cat. Each change is the <see cref="Domain.Players.Player"/>'s rule, saved; a
/// refused one throws a <see cref="Domain.DomainException"/> whose message is for the player.
/// </summary>
public interface IShopService
{
    Task<Wardrobe> GetAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// Buys the item with the player's coins, from the catalogue in the room <paramref name="roomId"/> (of the world's
    /// rooms; null when the cat is in none, or in a home): anywhere but the shop is refused. Their coins never go below
    /// zero, however many purchases and catches land at once: a purchase decided on coins that changed meanwhile is
    /// decided again.
    /// </summary>
    Task<Wardrobe> BuyAsync(Guid playerId, string? roomId, string itemId, CancellationToken cancellationToken);

    /// <summary>Puts on a hat, collar or fur the player has.</summary>
    Task<Wardrobe> WearAsync(Guid playerId, string itemId, CancellationToken cancellationToken);

    /// <summary>Takes off the hat or collar the player's cat wears.</summary>
    Task<Wardrobe> TakeOffAsync(Guid playerId, string itemId, CancellationToken cancellationToken);
}
