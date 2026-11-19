namespace Pspsps.Domain.Moderation;

/// <summary>
/// A network banned with a banned player, so the player can't come back as a new guest from it: an aggregate of its own,
/// keyed by the network's keyed hash (<see cref="AddressHashes"/>, never the address). Only an admin bans one (a
/// <see cref="Moderator"/>), never an admin's nor their own, only a network the player was seen on lately
/// (<see cref="PlayerAddress"/>), and only while the player is banned: it takes that ban's reason, and lasts a day or a
/// week (<see cref="Lengths"/>), never longer than the player's ban and never for good, since a network can be shared
/// (a school, a phone network). It ends at its time by itself, or when an admin lifts it; a new one takes the place of the
/// last. Each ban and lift is a line of the banned player's audit trail.
/// </summary>
public sealed class AddressBan
{
    private readonly List<ModerationEntry> _entries = [];

    private AddressBan()
    {
        AddressHash = "";
    }

    private AddressBan(string addressHash)
    {
        AddressHash = addressHash;
    }

    /// <summary>How long a network's ban lasts: a day or a week. Never for good.</summary>
    public static IReadOnlyList<TimeSpan> Lengths { get; } = [TimeSpan.FromHours(24), TimeSpan.FromDays(7)];

    /// <summary>The network's keyed hash.</summary>
    public string AddressHash { get; private set; }

    /// <summary>What admins see of the network: the first 8 hex digits of its hash.</summary>
    public string Label => AddressHashes.Label(AddressHash);

    /// <summary>The banned player whose network it is.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Why: the player's ban's reason, which everyone refused on the network is shown.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset BannedAt { get; private set; }

    /// <summary>When it ends: always a time.</summary>
    public DateTimeOffset BannedUntil { get; private set; }

    public Guid BannedBy { get; private set; }

    /// <summary>When an admin lifted it (null: not lifted).</summary>
    public DateTimeOffset? LiftedAt { get; private set; }

    public Guid? LiftedBy { get; private set; }

    /// <summary>Counts every change: two admins acting at once are decided one after the other.</summary>
    public int Version { get; private set; }

    /// <summary>The audit lines added since it was read (the player's trail, <see cref="ModerationEntry"/>).</summary>
    public IReadOnlyCollection<ModerationEntry> Entries => _entries.AsReadOnly();

    /// <summary>The last ban, whether or not it has ended; null once lifted.</summary>
    public Sanction? Ban => Reason is { } reason && LiftedAt is null ? new Sanction(reason, BannedAt, BannedUntil, BannedBy) : null;

    /// <summary>A network no admin has banned yet.</summary>
    public static AddressBan For(string addressHash) => new(AddressHashes.Valid(addressHash));

    /// <summary>The ban in force at <paramref name="now"/>, if any.</summary>
    public Sanction? BanAt(DateTimeOffset now) => Ban is { } ban && ban.InForceAt(now) ? ban : null;

    /// <summary>
    /// Bans the network for <paramref name="length"/> (one of <see cref="Lengths"/>), with the player it was
    /// <paramref name="seen"/> on, who must be banned now (<paramref name="player"/>): the network's ban takes that ban's
    /// reason, and ends with it if that ends sooner.
    /// </summary>
    public void BanWith(Moderator by, PlayerAddress seen, PlayerModeration player, TimeSpan length, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(player);
        if (seen.AddressHash != AddressHash || seen.PlayerId != player.PlayerId)
        {
            throw new DomainException("That cat wasn't seen on that network.");
        }

        if (by.PlayerId == player.PlayerId)
        {
            throw new DomainException("You can't moderate yourself.");
        }

        if (by.IsAdmin(player.PlayerId))
        {
            throw new DomainException("That cat is an admin. Admins are changed in the server's settings, not moderated.");
        }

        if (!seen.IsRecentAt(now))
        {
            throw new DomainException($"That cat wasn't on that network in the last {PlayerAddress.KeepFor.Days} days.");
        }

        var ban = player.BanAt(now) ?? throw new DomainException("Ban the cat first: a network is banned with its cat.");
        if (!Lengths.Contains(length))
        {
            throw new DomainException("A network's ban lasts a day or a week.");
        }

        var until = now + length;
        if (ban.Until is { } catsEnd && catsEnd < until)
        {
            until = catsEnd;
        }

        var reason = PlayerModeration.ReasonOf(ban.Reason);
        (PlayerId, Reason, BannedAt, BannedUntil, BannedBy, LiftedAt, LiftedBy) =
            (player.PlayerId, reason, now, until, by.PlayerId, null, null);
        Record(ModerationKind.AddressBanned, by, now, reason, until);
    }

    /// <summary>Lifts the ban in force; refused when there is none.</summary>
    public void Lift(Moderator by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (BanAt(now) is null)
        {
            throw new DomainException("That network isn't banned.");
        }

        (LiftedAt, LiftedBy) = (now, by.PlayerId);
        Record(ModerationKind.AddressBanLifted, by, now, reason: null, until: null);
    }

    private void Record(ModerationKind kind, Moderator by, DateTimeOffset now, string? reason, DateTimeOffset? until)
    {
        _entries.Add(new ModerationEntry(PlayerId, kind, by.PlayerId, now, reason, until, Label));
        Version++;
    }
}
