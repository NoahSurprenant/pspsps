namespace Pspsps.Domain.Shop;

/// <summary>
/// What an item is, which says where it goes: a cat wears one hat, one collar, and always exactly one fur; furniture
/// goes in the player's home (<see cref="Homes.Home"/>).
/// </summary>
public enum ItemKind
{
    Hat,
    Collar,
    Fur,
    Furniture,
}
