using Pspsps.Domain.Players;

namespace Pspsps.Domain.Shop;

/// <summary>
/// Something in the shop: a hat, a collar, a fur colour or a piece of furniture, and what it costs. A fur item
/// (<see cref="Fur"/>) changes the cat's coat when worn. A piece of furniture's ID is the kind of prop it is drawn as
/// (<see cref="Rooms.PropKinds"/>), and <see cref="Radius"/> is the circle it takes up on a home's floor.
/// </summary>
public sealed record ShopItem(string Id, string Name, ItemKind Kind, int Price, FurColor? Fur = null, double Radius = 0)
{
    public bool IsFurniture => Kind == ItemKind.Furniture;
}
