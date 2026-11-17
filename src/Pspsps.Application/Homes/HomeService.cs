using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Homes;

internal sealed class HomeService(IApplicationDbContext db, TimeProvider time) : IHomeService
{
    /// <summary>How many times a change is decided again when the home changed meanwhile, before giving up.</summary>
    internal const int Attempts = 5;

    private const string NoSuchCat = "That cat doesn't exist.";

    public async Task<HomePlan> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var name = await OwnerNameAsync(ownerId, cancellationToken);
        return HomePlan.Of(await ReadAsync(ownerId, cancellationToken), name);
    }

    public async Task<HomePlan> VisitAsync(Guid visitorId, Guid ownerId, CancellationToken cancellationToken)
    {
        var name = await OwnerNameAsync(ownerId, cancellationToken);
        var home = await ReadAsync(ownerId, cancellationToken);
        var friends = visitorId != ownerId && (await FriendsOfAsync(visitorId, [ownerId], cancellationToken)).Count > 0;
        home.EnsureCanVisit(visitorId, friends);
        return HomePlan.Of(home, name);
    }

    public async Task<IReadOnlySet<Guid>> OpenToAsync(
        Guid visitorId, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);
        var ids = ownerIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var homes = await db.Homes.AsNoTracking()
            .Where(h => ids.Contains(h.OwnerId))
            .ToDictionaryAsync(h => h.OwnerId, cancellationToken);
        var friends = await FriendsOfAsync(visitorId, ids, cancellationToken);
        return ids
            .Where(id => (homes.GetValueOrDefault(id) ?? Home.Create(id)).MayVisit(visitorId, friends.Contains(id)))
            .ToHashSet();
    }

    public Task<HomePlan> PlaceAsync(
        Guid ownerId, string itemId, double? x, double? z, int quarterTurns, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, (home, owner) =>
        {
            var owned = owner.CountOf(itemId);
            if (x is { } across && z is { } deep)
            {
                home.Place(itemId, owned, new Position(across, deep), quarterTurns, time.GetUtcNow());
            }
            else
            {
                home.PlaceAnywhere(itemId, owned, time.GetUtcNow());
            }
        }, cancellationToken);

    public Task<HomePlan> TurnAsync(Guid ownerId, Guid pieceId, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, (home, _) => home.Turn(pieceId), cancellationToken);

    public Task<HomePlan> PutAwayAsync(Guid ownerId, Guid pieceId, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, (home, _) => home.PutAway(pieceId), cancellationToken);

    public Task<HomePlan> LetInAsync(Guid ownerId, HomeVisitors visitors, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, (home, _) => home.LetIn(visitors), cancellationToken);

    public async Task<IReadOnlyList<Guid>> MustLeaveAsync(
        Guid ownerId, IReadOnlyCollection<Guid> here, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(here);
        var home = await ReadAsync(ownerId, cancellationToken);
        var friends = await FriendsOfAsync(ownerId, [.. here], cancellationToken);
        return [.. here.Where(id => !home.MayVisit(id, friends.Contains(id)))];
    }

    // Reads the home (a new one when the owner never changed theirs), applies the change, saves. The home's Version is a
    // concurrency token: a change decided on a home that changed meanwhile (another tab) is refused by the database and
    // decided again on the home as it is now, so two pieces put out at once can't overlap. Two tabs making the first
    // change at once both add the home: the second is refused by its key, and decided again too.
    private async Task<HomePlan> ChangeAsync(
        Guid ownerId, Action<Home, Player> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var owner = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ownerId, cancellationToken)
                ?? throw new DomainException("Your cat wasn't found. Sign in again.");
            var home = await db.Homes.FirstOrDefaultAsync(h => h.OwnerId == ownerId, cancellationToken);
            if (home is null)
            {
                home = Home.Create(ownerId);
                db.Homes.Add(home);
            }

            try
            {
                change(home, owner);
                await db.SaveChangesAsync(cancellationToken);
                return HomePlan.Of(home, owner.DisplayName);
            }
            catch (DomainException)
            {
                db.Forget(home);
                throw;
            }
            catch (DbUpdateException saving) when (saving is DbUpdateConcurrencyException || db.IsUniqueViolation(saving))
            {
                db.Forget(home);
                if (attempt == Attempts)
                {
                    throw new DomainException("That changed just now. Try again.");
                }
            }
        }
    }

    private async Task<Home> ReadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Homes.AsNoTracking().FirstOrDefaultAsync(h => h.OwnerId == ownerId, cancellationToken)
        ?? Home.Create(ownerId);

    private async Task<string> OwnerNameAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Players.Where(p => p.Id == ownerId).Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(NoSuchCat);

    // Which of <among> are the player's friends (accepted, not requests either way): only those friendships are read.
    private async Task<HashSet<Guid>> FriendsOfAsync(Guid playerId, List<Guid> among, CancellationToken cancellationToken)
    {
        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted && (
                (f.FirstPlayerId == playerId && among.Contains(f.SecondPlayerId))
                || (f.SecondPlayerId == playerId && among.Contains(f.FirstPlayerId))))
            .Select(f => f.FirstPlayerId == playerId ? f.SecondPlayerId : f.FirstPlayerId)
            .ToListAsync(cancellationToken);
        return [.. friends];
    }
}
