using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Friends;

internal sealed class FriendService(IApplicationDbContext db, TimeProvider time) : IFriendService
{
    private const string NoSuchCat = "That cat doesn't exist.";
    private const string Changed = "That changed just now. Look again.";

    public async Task<PlayerRelation?> GetAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        var other = await FindPlayerAsync(otherId, cancellationToken);
        if (other is null)
        {
            return null;
        }

        var friendship = await FindAsync(playerId, otherId, tracked: false, cancellationToken);
        return Relation(other, friendship?.StateFor(playerId) ?? FriendState.None);
    }

    public async Task<PlayerRelation> RequestAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        var other = await FindPlayerAsync(otherId, cancellationToken) ?? throw new DomainException(NoSuchCat);
        var now = time.GetUtcNow();
        if (await FindAsync(playerId, otherId, tracked: true, cancellationToken) is { } existing)
        {
            existing.RequestAgain(
                playerId,
                now,
                await CountsAsync(playerId, cancellationToken),
                await CountsAsync(otherId, cancellationToken));
            await SaveAsync(cancellationToken);
            return Relation(other, existing.StateFor(playerId));
        }

        var friendship = Friendship.Request(
            playerId, otherId,
            await CountsAsync(playerId, cancellationToken),
            await CountsAsync(otherId, cancellationToken),
            now);
        db.Friendships.Add(friendship);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException saving) when (db.IsUniqueViolation(saving))
        {
            // The other asked at the same moment (or this player, from another tab): theirs is the one there is now.
            db.Forget(friendship);
            var winner = await FindAsync(playerId, otherId, tracked: true, cancellationToken)
                ?? throw new DomainException(Changed);
            winner.RequestAgain(
                playerId,
                now,
                await CountsAsync(playerId, cancellationToken),
                await CountsAsync(otherId, cancellationToken));
            await SaveAsync(cancellationToken);
            return Relation(other, winner.StateFor(playerId));
        }

        return Relation(other, friendship.StateFor(playerId));
    }

    public async Task<PlayerRelation> AcceptAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        var mine = await CountsAsync(playerId, cancellationToken);
        var theirs = await CountsAsync(otherId, cancellationToken);
        return await ChangeAsync(playerId, otherId, friendship =>
        {
            friendship.Accept(playerId, time.GetUtcNow(), mine, theirs);
            return false;
        }, cancellationToken);
    }

    public Task<PlayerRelation> DeclineAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, otherId, friendship =>
        {
            friendship.Decline(playerId);
            return true;
        }, cancellationToken);

    public Task<PlayerRelation> CancelAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, otherId, friendship =>
        {
            friendship.Cancel(playerId);
            return true;
        }, cancellationToken);

    public Task<PlayerRelation> UnfriendAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, otherId, friendship =>
        {
            friendship.Unfriend(playerId);
            return true;
        }, cancellationToken);

    public async Task<PlayerRelation> VisitAsync(Guid playerId, Guid friendId, CancellationToken cancellationToken)
    {
        var friend = await FindPlayerAsync(friendId, cancellationToken) ?? throw new DomainException(NoSuchCat);
        var friendship = await FindAsync(playerId, friendId, tracked: false, cancellationToken)
            ?? throw new DomainException("You can only go to your friends.");
        friendship.EnsureCanVisit(playerId);
        return Relation(friend, FriendState.Friends);
    }

    public async Task<IReadOnlyList<PlayerRelation>> ListAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var friendships = await Of(playerId).AsNoTracking().ToListAsync(cancellationToken);
        var others = friendships.Select(f => f.OtherThan(playerId)).ToList();
        var players = await Cats(db.Players.Where(p => others.Contains(p.Id)))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        return
        [
            .. friendships
                .Where(f => players.ContainsKey(f.OtherThan(playerId)))
                .Select(f => Relation(players[f.OtherThan(playerId)], f.StateFor(playerId)))
                .OrderBy(r => ListOrder(r.State))
                .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PlayerId),
        ];
    }

    // Accepts, or checks and deletes (when the change returns true), the pair's friendship.
    private async Task<PlayerRelation> ChangeAsync(
        Guid playerId, Guid otherId, Func<Friendship, bool> change, CancellationToken cancellationToken)
    {
        var other = await FindPlayerAsync(otherId, cancellationToken) ?? throw new DomainException(NoSuchCat);
        var friendship = await FindAsync(playerId, otherId, tracked: true, cancellationToken)
            ?? throw new DomainException("There's nothing between you and that cat.");
        if (change(friendship))
        {
            db.Friendships.Remove(friendship);
            await SaveAsync(cancellationToken);
            return Relation(other, FriendState.None);
        }

        await SaveAsync(cancellationToken);
        return Relation(other, friendship.StateFor(playerId));
    }

    // A friendship the other changed or deleted meanwhile (a request taken back as it's accepted): nothing is changed.
    // Status is a concurrency token, so a decision made on the old status can't overwrite or delete the new one.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(Changed);
        }
    }

    private IQueryable<Friendship> Of(Guid playerId) =>
        db.Friendships.Where(f => f.FirstPlayerId == playerId || f.SecondPlayerId == playerId);

    private async Task<FriendCounts> CountsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var all = await Of(playerId)
            .Select(f => new { f.Status, Sent = f.RequesterId == playerId })
            .ToListAsync(cancellationToken);
        return new FriendCounts(
            all.Count(f => f.Status == FriendshipStatus.Accepted),
            all.Count(f => f.Status == FriendshipStatus.Pending && f.Sent),
            all.Count(f => f.Status == FriendshipStatus.Pending && !f.Sent));
    }

    private Task<Friendship?> FindAsync(Guid playerId, Guid otherId, bool tracked, CancellationToken cancellationToken)
    {
        var (first, second) = Friendship.PairOf(playerId, otherId);
        var query = tracked ? db.Friendships : db.Friendships.AsNoTracking();
        return query.FirstOrDefaultAsync(
            f => f.FirstPlayerId == first && f.SecondPlayerId == second, cancellationToken);
    }

    private Task<Cat?> FindPlayerAsync(Guid playerId, CancellationToken cancellationToken) =>
        Cats(db.Players.Where(p => p.Id == playerId)).FirstOrDefaultAsync(cancellationToken);

    // Other cats as a card shows them, read on their own: loading whole players would read every login and item they
    // have as well (for every friend in a list). Filter the players first: the projection can't be.
    private static IQueryable<Cat> Cats(IQueryable<Player> players) =>
        players.Select(p => new Cat(p.Id, p.DisplayName, p.FurColor, p.Hat, p.Collar));

    private static PlayerRelation Relation(Cat other, FriendState state) =>
        new(other.Id, other.DisplayName, other.FurColor, state, other.Hat, other.Collar);

    private sealed record Cat(Guid Id, string DisplayName, FurColor FurColor, string? Hat, string? Collar);

    private static int ListOrder(FriendState state) => state switch
    {
        FriendState.Friends => 0,
        FriendState.RequestReceived => 1,
        _ => 2,
    };
}
