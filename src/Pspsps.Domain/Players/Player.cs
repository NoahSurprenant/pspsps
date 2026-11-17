namespace Pspsps.Domain.Players;

/// <summary>A cat: the aggregate root for everything a player keeps between visits.</summary>
public sealed class Player
{
    private readonly List<PlayerLogin> _logins = [];

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
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; }

    public FurColor FurColor { get; private set; }

    public int Coins { get; private set; }

    /// <summary>A guest has no login: the account lasts as long as the browser keeps its cookie.</summary>
    public bool IsGuest { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<PlayerLogin> Logins => _logins.AsReadOnly();

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

    public void ChangeFurColor(FurColor furColor)
    {
        if (!Enum.IsDefined(furColor))
        {
            throw new DomainException("That fur color doesn't exist.");
        }

        FurColor = furColor;
    }

    public void EarnCoins(int amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Coins earned must be more than zero.");
        }

        Coins = checked(Coins + amount);
    }

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
