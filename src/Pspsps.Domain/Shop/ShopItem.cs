using Pspsps.Domain.Players;

namespace Pspsps.Domain.Shop;

/// <summary>
/// Something in the shop: a hat, a collar or a fur colour, and what it costs. A fur item (<see cref="Fur"/>) changes the
/// cat's coat when worn.
/// </summary>
public sealed record ShopItem(string Id, string Name, ItemKind Kind, int Price, FurColor? Fur = null);
