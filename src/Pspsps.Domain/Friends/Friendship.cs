namespace Pspsps.Domain.Friends;

/// <summary>
/// Two players' friendship, from the request on: an aggregate of its own, not part of either <see
/// cref="Players.Player"/>. A pair of players has at most one, whoever asked: its key is the pair in a fixed order
/// (<see cref="FirstPlayerId"/>, <see cref="SecondPlayerId"/>), so the database refuses a second one even when both ask
/// at once. A declined, taken back or ended friendship is deleted, so either can ask again later.
/// </summary>
public sealed class Friendship
{
    /// <summary>How many friendships (friends and requests, both ways) a player can have.</summary>
    public const int MaxPerPlayer = 100;

    private Friendship()
    {
    }

    private Friendship(Guid requesterId, Guid addresseeId, DateTimeOffset now)
    {
        (FirstPlayerId, SecondPlayerId) = PairOf(requesterId, addresseeId);
        RequesterId = requesterId;
        Status = FriendshipStatus.Pending;
        RequestedAt = now;
    }

    /// <summary>The one of the two players whose ID sorts first.</summary>
    public Guid FirstPlayerId { get; private set; }

    /// <summary>The one of the two players whose ID sorts second.</summary>
    public Guid SecondPlayerId { get; private set; }

    /// <summary>Who asked.</summary>
    public Guid RequesterId { get; private set; }

    /// <summary>Who was asked.</summary>
    public Guid AddresseeId => RequesterId == FirstPlayerId ? SecondPlayerId : FirstPlayerId;

    public FriendshipStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When the addressee accepted; null while the request is pending.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>A pair of players in the order a friendship keys them by, whoever asked.</summary>
    public static (Guid First, Guid Second) PairOf(Guid one, Guid other) =>
        one.CompareTo(other) <= 0 ? (one, other) : (other, one);

    /// <summary>
    /// <paramref name="requesterId"/> asks <paramref name="addresseeId"/> to be friends. Each has
    /// <paramref name="requesterCount"/> and <paramref name="addresseeCount"/> friendships already (friends and
    /// requests); neither may be at <see cref="MaxPerPlayer"/>. For a pair that already has a friendship, use
    /// <see cref="RequestAgain"/> on it instead.
    /// </summary>
    public static Friendship Request(
        Guid requesterId, Guid addresseeId, int requesterCount, int addresseeCount, DateTimeOffset now)
    {
        if (requesterId == Guid.Empty || addresseeId == Guid.Empty)
        {
            throw new DomainException("That cat doesn't exist.");
        }

        if (requesterId == addresseeId)
        {
            throw new DomainException("You can't be friends with yourself.");
        }

        if (requesterCount >= MaxPerPlayer)
        {
            throw new DomainException($"You have {MaxPerPlayer} friends and requests already. Remove some first.");
        }

        if (addresseeCount >= MaxPerPlayer)
        {
            throw new DomainException("That cat can't take any more friends.");
        }

        return new Friendship(requesterId, addresseeId, now);
    }

    /// <summary>Whether <paramref name="playerId"/> is one of the two.</summary>
    public bool Involves(Guid playerId) => playerId == FirstPlayerId || playerId == SecondPlayerId;

    /// <summary>The other one of the two, seen from <paramref name="playerId"/>.</summary>
    public Guid OtherThan(Guid playerId) =>
        playerId == FirstPlayerId ? SecondPlayerId
        : playerId == SecondPlayerId ? FirstPlayerId
        : throw new DomainException("That isn't your friendship.");

    /// <summary>Where <paramref name="playerId"/> stands in this friendship.</summary>
    public FriendState StateFor(Guid playerId)
    {
        if (!Involves(playerId))
        {
            return FriendState.None;
        }

        if (Status == FriendshipStatus.Accepted)
        {
            return FriendState.Friends;
        }

        return playerId == RequesterId ? FriendState.RequestSent : FriendState.RequestReceived;
    }

    /// <summary>
    /// <paramref name="playerId"/> asks again where this friendship already exists. Asking someone who has asked you is
    /// accepting; asking twice, or asking a friend, is refused.
    /// </summary>
    public void RequestAgain(Guid playerId, DateTimeOffset now)
    {
        switch (StateFor(playerId))
        {
            case FriendState.RequestReceived:
                Accept(playerId, now);
                break;
            case FriendState.RequestSent:
                throw new DomainException("You've already asked. Wait for an answer.");
            case FriendState.Friends:
                throw new DomainException("You're already friends.");
            default:
                throw new DomainException("That isn't your friendship.");
        }
    }

    /// <summary>The addressee, and only they, accept a pending request.</summary>
    public void Accept(Guid playerId, DateTimeOffset now)
    {
        EnsureReceived(playerId);
        Status = FriendshipStatus.Accepted;
        AcceptedAt = now;
    }

    /// <summary>
    /// Checks that <paramref name="playerId"/> may decline: the addressee of a pending request. A declined request is
    /// deleted (by the caller), so nothing is kept of it.
    /// </summary>
    public void Decline(Guid playerId) => EnsureReceived(playerId);

    /// <summary>Checks that <paramref name="playerId"/> may take the request back: its requester, while it's pending.</summary>
    public void Cancel(Guid playerId)
    {
        if (StateFor(playerId) != FriendState.RequestSent)
        {
            throw new DomainException("There's no request of yours to take back.");
        }
    }

    /// <summary>Checks that <paramref name="playerId"/> may end the friendship: either friend.</summary>
    public void Unfriend(Guid playerId)
    {
        if (StateFor(playerId) != FriendState.Friends)
        {
            throw new DomainException("You aren't friends.");
        }
    }

    private void EnsureReceived(Guid playerId)
    {
        if (StateFor(playerId) != FriendState.RequestReceived)
        {
            throw new DomainException("There's no request to you to answer.");
        }
    }
}
