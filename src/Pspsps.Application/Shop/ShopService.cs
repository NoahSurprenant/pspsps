using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Shop;

internal sealed class ShopService(IApplicationDbContext db, TimeProvider time) : IShopService
{
    /// <summary>How many times a change is decided again when the player changed meanwhile, before giving up.</summary>
    internal const int Attempts = 5;

    private const string NotFound = "Your cat wasn't found. Sign in again.";

    public async Task<Wardrobe> GetAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
            ?? throw new DomainException(NotFound);
        return Wardrobe.Of(player);
    }

    public Task<Wardrobe> BuyAsync(Guid playerId, string itemId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, player => player.Buy(itemId, time.GetUtcNow()), cancellationToken);

    public Task<Wardrobe> WearAsync(Guid playerId, string itemId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, player => player.Wear(itemId), cancellationToken);

    public Task<Wardrobe> TakeOffAsync(Guid playerId, string itemId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, player => player.TakeOff(itemId), cancellationToken);

    // Reads the player, applies the change, saves. Coins are a concurrency token: when they changed between the read and
    // the save (a catch, a purchase in another tab), the save is refused and the change decided again on the player as
    // they are now, so a purchase never spends coins that are gone, nor saves over coins just earned. The same item
    // bought twice at once is refused by the database's key, and decided again too (the second finds it owned).
    private async Task<Wardrobe> ChangeAsync(Guid playerId, Action<Player> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
                ?? throw new DomainException(NotFound);
            try
            {
                change(player);
                await db.SaveChangesAsync(cancellationToken);
                return Wardrobe.Of(player);
            }
            catch (DomainException)
            {
                db.Forget(player);
                throw;
            }
            catch (DbUpdateException saving) when (saving is DbUpdateConcurrencyException || db.IsUniqueViolation(saving))
            {
                db.Forget(player);
                if (attempt == Attempts)
                {
                    throw new DomainException("That changed just now. Try again.");
                }
            }
        }
    }
}
