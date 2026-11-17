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
            existing.RequestAgain(playerId, now);
            await SaveAsync(cancellationToken);
            return Relation(other, existing.StateFor(playerId));
        }

        var friendship = Friendship.Request(
            playerId, otherId,
            await CountAsync(playerId, cancellationToken),
            await CountAsync(otherId, cancellationToken),
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
            winner.RequestAgain(playerId, now);
            await SaveAsync(cancellationToken);
            return Relation(other, winner.StateFor(playerId));
        }

        return Relation(other, friendship.StateFor(playerId));
    }

    public Task<PlayerRelation> AcceptAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, otherId, friendship =>
        {
            friendship.Accept(playerId, time.GetUtcNow());
            return false;
        }, cancellationToken);

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

    public async Task<IReadOnlyList<PlayerRelation>> ListAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var friendships = await Of(playerId).AsNoTracking().ToListAsync(cancellationToken);
        var others = friendships.Select(f => f.OtherThan(playerId)).ToList();
        var players = await db.Players.AsNoTracking()
            .Where(p => others.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
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

    // A friendship changed or deleted by the other meanwhile (a request taken back as it's accepted) is no change at all.
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

    private Task<int> CountAsync(Guid playerId, CancellationToken cancellationToken) =>
        Of(playerId).CountAsync(cancellationToken);

    private Task<Friendship?> FindAsync(Guid playerId, Guid otherId, bool tracked, CancellationToken cancellationToken)
    {
        var (first, second) = Friendship.PairOf(playerId, otherId);
        var query = tracked ? db.Friendships : db.Friendships.AsNoTracking();
        return query.FirstOrDefaultAsync(f => f.FirstPlayerId == first && f.SecondPlayerId == second, cancellationToken);
    }

    private Task<Player?> FindPlayerAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

    private static PlayerRelation Relation(Player other, FriendState state) =>
        new(other.Id, other.DisplayName, other.FurColor, state);

    private static int ListOrder(FriendState state) => state switch
    {
        FriendState.Friends => 0,
        FriendState.RequestReceived => 1,
        _ => 2,
    };
}
