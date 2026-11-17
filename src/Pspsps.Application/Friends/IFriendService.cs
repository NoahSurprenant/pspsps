namespace Pspsps.Application.Friends;

/// <summary>
/// Friend requests and friendships. Every method is from <c>playerId</c>'s side, about <c>otherId</c>; a rule broken
/// (asking yourself, asking twice, answering a request that isn't yours) is a <see cref="Domain.DomainException"/>.
/// </summary>
public interface IFriendService
{
    /// <summary>The other player as this one sees them (a player card), or null when there's no such player.</summary>
    Task<PlayerRelation?> GetAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Asks the other player to be friends; asking someone who has asked you accepts their request.</summary>
    Task<PlayerRelation> RequestAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Accepts the other player's request.</summary>
    Task<PlayerRelation> AcceptAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Declines the other player's request; nothing is kept of it.</summary>
    Task<PlayerRelation> DeclineAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Takes back this player's request to the other.</summary>
    Task<PlayerRelation> CancelAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Ends a friendship.</summary>
    Task<PlayerRelation> UnfriendAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>The player's friends and requests both ways: friends first, then requests to them, then theirs; by name.</summary>
    Task<IReadOnlyList<PlayerRelation>> ListAsync(Guid playerId, CancellationToken cancellationToken);
}
