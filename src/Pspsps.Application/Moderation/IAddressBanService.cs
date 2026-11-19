using Pspsps.Domain.Moderation;

namespace Pspsps.Application.Moderation;

/// <summary>
/// The networks players play from, as keyed hashes (<see cref="AddressHashes"/>: the server makes them; the address itself
/// is never kept), and the bans admins put on them with a banned player (<see cref="AddressBan"/>).
/// </summary>
public interface IAddressBanService
{
    /// <summary>The player was seen on the network <paramref name="addressHash"/> now: remembered for 7 days.</summary>
    Task SeenAsync(Guid playerId, string addressHash, CancellationToken cancellationToken);

    /// <summary>The network's ban in force now, if any.</summary>
    Task<Sanction?> BanOfAsync(string addressHash, CancellationToken cancellationToken);

    /// <summary>For an admin: the networks the player was seen on lately, newest first, with how many players each.</summary>
    Task<IReadOnlyList<RecentAddress>> RecentOfAsync(Guid adminId, Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="adminId"/> bans the networks <paramref name="addressHashes"/> the player, who is banned, was seen on
    /// lately, for <paramref name="length"/> (the rules: <see cref="AddressBan"/>); the bans as decided.
    /// </summary>
    Task<IReadOnlyList<BannedAddress>> BanAsync(
        Guid adminId, Guid playerId, IReadOnlyCollection<string> addressHashes, TimeSpan length,
        CancellationToken cancellationToken);

    /// <summary>Lifts the bans in force on the networks banned with the player; the networks lifted (none: nothing to lift).</summary>
    Task<IReadOnlyList<string>> LiftOfPlayerAsync(Guid adminId, Guid playerId, CancellationToken cancellationToken);

    /// <summary>Lifts the network's ban in force; refused when there is none.</summary>
    Task LiftAsync(Guid adminId, string addressHash, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets the networks not seen for 7 days, and the bans that ended or were lifted (their audit lines stay, with the
    /// network's label only); how many rows went.
    /// </summary>
    Task<int> PruneAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A network a player was seen on lately, for an admin: its hash (to ban it by) and label (the hash's first 8 hex digits:
/// never the address), how many players were seen on it in the last 7 days, when this player last was, and its ban in force.
/// </summary>
public sealed record RecentAddress(string AddressHash, string Label, int Players, DateTimeOffset LastSeen, Sanction? Ban);

/// <summary>A network banned: its hash and label, and the ban.</summary>
public sealed record BannedAddress(string AddressHash, string Label, Sanction Ban);

/// <summary>A network banned now, for the admin page: whose it was, how many players were on it lately, and who banned it.</summary>
public sealed record AddressBanLine(
    string AddressHash, string Label, Guid PlayerId, string PlayerName, int Players, Sanction Ban, string ByName);
