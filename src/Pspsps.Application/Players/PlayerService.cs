using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

internal sealed class PlayerService(IApplicationDbContext db, TimeProvider time, Random random) : IPlayerService
{
    /// <summary>How many times a link is done again when the player's coins changed as it was saved.</summary>
    private const int LinkAttempts = 5;

    public async Task<PlayerProfile> SignInWithLoginAsync(
        LoginProvider provider, string providerKey, string? profileName, CancellationToken cancellationToken)
    {
        var existing = await OwnerAsync(provider, providerKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var name = DisplayNameRules.FromProfile(profileName, fallback: RandomCatName());
        var player = Player.CreateWithLogin(provider, providerKey, name, RandomFur(), time.GetUtcNow());
        db.Players.Add(player);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException saving) when (db.IsUniqueViolation(saving))
        {
            // Another request gave the login a cat (a guest linking it, a sign-in in another browser) since the check
            // above: that cat is the one this login plays.
            db.Forget(player);
            var winner = await OwnerAsync(provider, providerKey, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return PlayerProfile.From(player);
    }

    // Coins are a concurrency token (a purchase must not spend coins that are gone): a link whose save finds them changed
    // meanwhile (a catch paid as the guest saves their cat) is refused by the database, and simply done again.
    public async Task<LinkLoginOutcome> LinkLoginAsync(
        Guid playerId, LoginProvider provider, string providerKey, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await LinkOnceAsync(playerId, provider, providerKey, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Forgotten already: the next try reads the player again.
                if (attempt == LinkAttempts)
                {
                    return LinkLoginOutcome.Busy;
                }
            }
        }
    }

    private async Task<LinkLoginOutcome> LinkOnceAsync(
        Guid playerId, LoginProvider provider, string providerKey, CancellationToken cancellationToken)
    {
        if (await OwnerIdAsync(provider, providerKey, cancellationToken) is { } owner)
        {
            return owner == playerId ? LinkLoginOutcome.Linked : LinkLoginOutcome.TakenByAnotherPlayer;
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
        {
            return LinkLoginOutcome.PlayerNotFound;
        }

        if (player.HasLoginFrom(provider))
        {
            return LinkLoginOutcome.Refused;
        }

        try
        {
            player.LinkLogin(provider, providerKey);
        }
        catch (DomainException)
        {
            return LinkLoginOutcome.InvalidLogin;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Forget(player);
            throw;
        }
        catch (DbUpdateException saving) when (db.IsUniqueViolation(saving))
        {
            // Another request got there first: the same link (Linked), this login for another cat (taken), or another
            // login from this provider for this cat (the database's one-per-provider index: refused).
            db.Forget(player);
            return await OwnerIdAsync(provider, providerKey, cancellationToken) switch
            {
                { } winner when winner == playerId => LinkLoginOutcome.Linked,
                not null => LinkLoginOutcome.TakenByAnotherPlayer,
                null => LinkLoginOutcome.Refused,
            };
        }

        return LinkLoginOutcome.Linked;
    }

    public async Task<PlayerProfile> CreateGuestAsync(CancellationToken cancellationToken)
    {
        var player = Player.CreateGuest(RandomCatName(), RandomFur(), time.GetUtcNow());
        db.Players.Add(player);
        await db.SaveChangesAsync(cancellationToken);
        return PlayerProfile.From(player);
    }

    public async Task<PlayerProfile?> FindAsync(Guid playerId, CancellationToken cancellationToken)
    {
        return await db.Players.Where(p => p.Id == playerId).Select(Profile).FirstOrDefaultAsync(cancellationToken);
    }

    // One statement adds the earning (coins = coins + n): two earnings at once (a tab replaced while its reel is paid, a
    // new tab on another pod) both count, and other saves of the player (a link, a rename) aren't refused because of it.
    public async Task<PlayerProfile> EarnCoinsAsync(Guid playerId, int coins, CancellationToken cancellationToken)
    {
        var earning = Player.Earning(coins);
        var paid = await db.Players
            .Where(p => p.Id == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Coins, p => p.Coins + earning), cancellationToken);
        if (paid == 0)
        {
            throw new DomainException("Your cat wasn't found. Sign in again.");
        }

        // The total as saved now: with any other earning that landed meanwhile.
        return await FindAsync(playerId, cancellationToken)
            ?? throw new DomainException("Your cat wasn't found. Sign in again.");
    }

    /// <summary>The player (at most one) who signs in with this login.</summary>
    private IQueryable<Player> Owning(LoginProvider provider, string providerKey) =>
        db.Players.Where(p => p.Logins.Any(l => l.Provider == provider && l.ProviderKey == providerKey));

    private Task<PlayerProfile?> OwnerAsync(
        LoginProvider provider, string providerKey, CancellationToken cancellationToken) =>
        Owning(provider, providerKey).Select(Profile).FirstOrDefaultAsync(cancellationToken);

    // A player as shown, read on its own: loading the whole aggregate would read every login and item it has as well.
    private static readonly Expression<Func<Player, PlayerProfile>> Profile = p => new PlayerProfile(
        p.Id, p.DisplayName, p.FurColor, p.Coins, p.IsGuest, p.Hat, p.Collar, p.OutfitVersion);

    private Task<Guid?> OwnerIdAsync(LoginProvider provider, string providerKey, CancellationToken cancellationToken) =>
        Owning(provider, providerKey).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);

    private string RandomCatName() =>
        "Kitty " + random.Next(1000, 10000).ToString(CultureInfo.InvariantCulture);

    private FurColor RandomFur()
    {
        var colors = Enum.GetValues<FurColor>();
        return colors[random.Next(colors.Length)];
    }
}
