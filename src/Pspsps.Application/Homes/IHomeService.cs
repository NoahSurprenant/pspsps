using Pspsps.Domain.Homes;

namespace Pspsps.Application.Homes;

/// <summary>
/// Players' homes: their furniture and who may visit. Each change is the <see cref="Home"/>'s rule, saved; a refused one
/// throws a <see cref="Domain.DomainException"/> whose message is for the player. Only the owner changes their home:
/// every change is from <c>ownerId</c>'s side.
/// </summary>
public interface IHomeService
{
    /// <summary>The player's home; a home never changed is empty, and open to their friends.</summary>
    Task<HomePlan> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks <paramref name="visitorId"/> may come into <paramref name="ownerId"/>'s home (refused when the owner doesn't
    /// let them in); returns the owner's name.
    /// </summary>
    Task<string> VisitAsync(Guid visitorId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="ownerIds"/> would let <paramref name="visitorId"/> into their homes now.</summary>
    Task<IReadOnlySet<Guid>> OpenToAsync(
        Guid visitorId, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken);

    /// <summary>
    /// Puts out a piece of furniture the owner has: at (<paramref name="x"/>, <paramref name="z"/>), turned
    /// <paramref name="quarterTurns"/> quarters; or, with no spot, wherever it fits.
    /// </summary>
    Task<HomePlan> PlaceAsync(
        Guid ownerId, string itemId, double? x, double? z, int quarterTurns, CancellationToken cancellationToken);

    /// <summary>Turns a piece a quarter.</summary>
    Task<HomePlan> TurnAsync(Guid ownerId, Guid pieceId, CancellationToken cancellationToken);

    /// <summary>Puts a piece away.</summary>
    Task<HomePlan> PutAwayAsync(Guid ownerId, Guid pieceId, CancellationToken cancellationToken);

    /// <summary>Changes who may visit.</summary>
    Task<HomePlan> LetInAsync(Guid ownerId, HomeVisitors visitors, CancellationToken cancellationToken);

    /// <summary>Which of the cats <paramref name="here"/>, in the owner's home, may not stay there now.</summary>
    Task<IReadOnlyList<Guid>> MustLeaveAsync(
        Guid ownerId, IReadOnlyCollection<Guid> here, CancellationToken cancellationToken);
}
