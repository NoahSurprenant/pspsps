using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;

namespace Pspsps.Domain.Players;

/// <summary>A cat: the aggregate root for everything a player keeps between visits.</summary>
public sealed class Player
{
    private readonly List<PlayerLogin> _logins = [];
    private readonly List<OwnedItem> _items = [];

    private Player()
    {
        DisplayName = null!;
    }

    private Player(string displayName, FurColor furColor, bool isGuest, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        DisplayName = DisplayNameRules.Normalize(displayName);
        FurColor = furColor;
        IsGuest = isGuest;
        CreatedAt = now;
        // The coat a cat is born with is its own: wearing another one later, it can always go back to it.
        _items.Add(new OwnedItem(ShopCatalog.FurOf(furColor).Id, now));
        // And so is its bed, for its home: no home starts with nothing to put in it.
        _items.Add(new OwnedItem(ShopCatalog.StarterFurniture, now));
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; }

    public FurColor FurColor { get; private set; }

    public int Coins { get; private set; }

    /// <summary>A guest has no login: the account lasts as long as the browser keeps its cookie.</summary>
    public bool IsGuest { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<PlayerLogin> Logins => _logins.AsReadOnly();

    /// <summary>The hat the cat wears (a <see cref="ShopCatalog"/> item ID), if any.</summary>
    public string? Hat { get; private set; }

    /// <summary>The collar the cat wears (a <see cref="ShopCatalog"/> item ID), if any.</summary>
    public string? Collar { get; private set; }

    /// <summary>
    /// Counts every change to what the cat wears (fur, hat, collar), so a room told of two changes out of order (or told
    /// one by a tab that read the player before the other) keeps the newer.
    /// </summary>
    public int OutfitVersion { get; private set; }

    /// <summary>What the player has from the shop: what they bought, and the coat they were born with.</summary>
    public IReadOnlyCollection<OwnedItem> Items => _items.AsReadOnly();

    public static Player CreateGuest(string displayName, FurColor furColor, DateTimeOffset now) =>
        new(displayName, furColor, isGuest: true, now);

    public static Player CreateWithLogin(
        LoginProvider provider, string providerKey, string displayName, FurColor furColor, DateTimeOffset now)
    {
        var player = new Player(displayName, furColor, isGuest: false, now);
        player._logins.Add(new PlayerLogin(provider, providerKey));
        return player;
    }

    /// <summary>
    /// Adds a Discord or Google login to this cat. A guest who gains one stops being a guest: the cat, its coins and
    /// everything else stay as they are. A cat has at most one login per provider.
    /// </summary>
    public void LinkLogin(LoginProvider provider, string providerKey)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new DomainException("That sign-in provider doesn't exist.");
        }

        if (HasLoginFrom(provider))
        {
            throw new DomainException($"This cat already has a {provider} login.");
        }

        _logins.Add(new PlayerLogin(provider, providerKey));
        IsGuest = false;
    }

    /// <summary>Whether the cat already signs in with an account from <paramref name="provider"/> (one per provider).</summary>
    public bool HasLoginFrom(LoginProvider provider) => _logins.Any(l => l.Provider == provider);

    public void Rename(string displayName) => DisplayName = DisplayNameRules.Normalize(displayName);

    /// <summary>Whether the player has the shop item <paramref name="itemId"/>.</summary>
    public bool Owns(string itemId) => _items.Any(i => i.ItemId == itemId);

    /// <summary>How many of the shop item <paramref name="itemId"/> the player has (0 when none).</summary>
    public int CountOf(string itemId) => _items.FirstOrDefault(i => i.ItemId == itemId)?.Count ?? 0;

    /// <summary>What a purchase from anywhere but the shop is told.</summary>
    public const string BuyInTheShop = "Things are bought from the catalogue in the Cat Shop, off Town Square.";

    /// <summary>
    /// Buys the shop item <paramref name="itemId"/>: the player pays its price from their coins, and has it from then on.
    /// Only from the shop's catalogue: <paramref name="room"/>, the room the player's cat is in (null when in none), must
    /// have it (<see cref="RoomDefinition.HasCatalogue"/>). Refused when they can't afford it, or have it already: one of
    /// anything a cat wears, and up to <see cref="ShopCatalog.MaxOfEachFurniture"/> of a piece of furniture. Buying
    /// doesn't put it on, or out.
    /// </summary>
    public ShopItem Buy(string itemId, RoomDefinition? room, DateTimeOffset now)
    {
        if (room is not { HasCatalogue: true })
        {
            throw new DomainException(BuyInTheShop);
        }

        var item = ShopCatalog.Find(itemId) ?? throw new DomainException("That isn't in the shop.");
        var owned = _items.FirstOrDefault(i => i.ItemId == item.Id);
        if (owned is not null && !item.IsFurniture)
        {
            throw new DomainException($"You already have the {item.Name}.");
        }

        if (owned is not null && owned.Count >= ShopCatalog.MaxOfEachFurniture)
        {
            throw new DomainException(
                $"You have {ShopCatalog.MaxOfEachFurniture} of the {item.Name} already: that's as many as a cat can keep.");
        }

        if (item.Price > Coins)
        {
            throw new DomainException($"The {item.Name} costs {item.Price} coins. You have {Coins}.");
        }

        SpendCoins(item.Price);
        if (owned is null)
        {
            _items.Add(new OwnedItem(item.Id, now));
        }
        else
        {
            owned.AddOne();
        }

        return item;
    }

    /// <summary>
    /// Puts on an item the player has: a hat in place of the one worn, a collar in place of the collar, a fur in place of
    /// the coat.
    /// </summary>
    public ShopItem Wear(string itemId)
    {
        var item = ShopCatalog.Find(itemId) ?? throw new DomainException("That isn't in the shop.");
        if (!Owns(item.Id))
        {
            throw new DomainException($"Buy the {item.Name} first.");
        }

        switch (item.Kind)
        {
            case ItemKind.Hat:
                Hat = item.Id;
                break;
            case ItemKind.Collar:
                Collar = item.Id;
                break;
            case ItemKind.Fur:
                FurColor = item.Fur ?? throw new InvalidOperationException($"Fur item '{item.Id}' has no fur.");
                break;
            case ItemKind.Furniture:
                throw new DomainException($"The {item.Name} goes in your home, not on your cat.");
            default:
                throw new InvalidOperationException($"Item '{item.Id}' is of no known kind.");
        }

        OutfitVersion++;
        return item;
    }

    /// <summary>Takes off the hat or collar <paramref name="itemId"/>. A cat always wears a fur: that is changed, not taken off.</summary>
    public void TakeOff(string itemId)
    {
        if (Hat is not null && Hat == itemId)
        {
            Hat = null;
            OutfitVersion++;
        }
        else if (Collar is not null && Collar == itemId)
        {
            Collar = null;
            OutfitVersion++;
        }
        else if (ShopCatalog.Find(itemId) is { Kind: ItemKind.Fur })
        {
            throw new DomainException("A cat always has its fur. Put on another to change it.");
        }
        else
        {
            throw new DomainException("You aren't wearing that.");
        }
    }

    public void EarnCoins(int amount) => Coins = checked(Coins + Earning(amount));

    /// <summary>
    /// What earning <paramref name="amount"/> adds to a player's coins: the rule for an earning that the database applies
    /// in one statement (coins = coins + this), so two earnings at once can't overwrite each other.
    /// </summary>
    public static int Earning(int amount) =>
        amount > 0 ? amount : throw new DomainException("Coins earned must be more than zero.");

    public void SpendCoins(int amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Coins spent must be more than zero.");
        }

        if (amount > Coins)
        {
            throw new DomainException("Not enough coins.");
        }

        Coins -= amount;
    }
}
