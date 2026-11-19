namespace Pspsps.Domain.Moderation;

/// <summary>
/// A network a player played from lately, as a keyed hash (<see cref="AddressHash"/>), never the address: kept for
/// <see cref="KeepFor"/> after it was last seen, so an admin can ban the networks of a player they ban, then forgotten.
/// One per player and network.
/// </summary>
public sealed class PlayerAddress
{
    /// <summary>How long a network is remembered after the player was last seen on it.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    /// <summary>A network seen again sooner than this after its last time isn't saved again: one write an hour at most.</summary>
    public static readonly TimeSpan SeenEvery = TimeSpan.FromHours(1);

    private PlayerAddress()
    {
        AddressHash = "";
    }

    private PlayerAddress(Guid playerId, string addressHash, DateTimeOffset now)
    {
        PlayerId = playerId;
        AddressHash = addressHash;
        LastSeen = now;
    }

    public Guid PlayerId { get; private set; }

    /// <summary>The network's keyed hash (<see cref="AddressHashes"/>).</summary>
    public string AddressHash { get; private set; }

    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>The player, seen on the network <paramref name="addressHash"/> at <paramref name="now"/>.</summary>
    public static PlayerAddress Seen(Guid playerId, string addressHash, DateTimeOffset now) =>
        playerId == Guid.Empty
            ? throw new DomainException("That cat doesn't exist.")
            : new PlayerAddress(playerId, AddressHashes.Valid(addressHash), now);

    /// <summary>The player was seen on it again at <paramref name="now"/>: whether that changed anything to save.</summary>
    public bool SeenAgain(DateTimeOffset now)
    {
        if (now - LastSeen < SeenEvery)
        {
            return false;
        }

        LastSeen = now;
        return true;
    }

    /// <summary>Whether it is still remembered at <paramref name="now"/> (older ones are pruned).</summary>
    public bool IsRecentAt(DateTimeOffset now) => now - LastSeen < KeepFor;

    /// <summary>The oldest last-seen time still remembered at <paramref name="now"/>.</summary>
    public static DateTimeOffset RecentSince(DateTimeOffset now) => now - KeepFor;
}

/// <summary>
/// A network as the server keeps it: a keyed hash (HMAC-SHA256, made by the server from the address and its secret key),
/// 64 lowercase hex digits. Admins see only its first 8 (<see cref="Label"/>).
/// </summary>
public static class AddressHashes
{
    public const int Length = 64;

    public const int LabelLength = 8;

    /// <summary>Whether <paramref name="hash"/> looks like one: 64 lowercase hex digits.</summary>
    public static bool IsValid(string? hash) =>
        hash is { Length: Length } && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary><paramref name="hash"/>, refused unless it is one.</summary>
    public static string Valid(string? hash) =>
        IsValid(hash) ? hash! : throw new DomainException("That isn't a network the server knows.");

    /// <summary>What admins see of it: its first 8 hex digits, never the address.</summary>
    public static string Label(string hash) => Valid(hash)[..LabelLength];
}
