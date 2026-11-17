using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

internal sealed class PlayerService(IApplicationDbContext db, TimeProvider time, Random random) : IPlayerService
{
    public async Task<PlayerProfile> SignInWithLoginAsync(
        LoginProvider provider, string providerKey, string? profileName, CancellationToken cancellationToken)
    {
        var existing = await db.Players
            .FirstOrDefaultAsync(
                p => p.Logins.Any(l => l.Provider == provider && l.ProviderKey == providerKey), cancellationToken);
        if (existing is not null)
        {
            return PlayerProfile.From(existing);
        }

        var name = DisplayNameRules.FromProfile(profileName, fallback: RandomCatName());
        var player = Player.CreateWithLogin(provider, providerKey, name, RandomFur(), time.GetUtcNow());
        db.Players.Add(player);
        await db.SaveChangesAsync(cancellationToken);
        return PlayerProfile.From(player);
    }

    public async Task<LinkLoginResult> LinkLoginAsync(
        Guid playerId, LoginProvider provider, string providerKey, CancellationToken cancellationToken)
    {
        var owner = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Logins.Any(l => l.Provider == provider && l.ProviderKey == providerKey), cancellationToken);
        if (owner is not null)
        {
            return owner.Id == playerId
                ? new LinkLoginResult(LinkLoginOutcome.Linked, PlayerProfile.From(owner))
                : new LinkLoginResult(
                    LinkLoginOutcome.TakenByAnotherPlayer, null, $"That {provider} account already has a cat.");
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
        {
            return new LinkLoginResult(LinkLoginOutcome.PlayerNotFound, null);
        }

        try
        {
            player.LinkLogin(provider, providerKey);
        }
        catch (DomainException refused)
        {
            return new LinkLoginResult(LinkLoginOutcome.Refused, PlayerProfile.From(player), refused.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new LinkLoginResult(LinkLoginOutcome.Linked, PlayerProfile.From(player));
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
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        return player is null ? null : PlayerProfile.From(player);
    }

    private string RandomCatName() =>
        "Kitty " + random.Next(1000, 10000).ToString(CultureInfo.InvariantCulture);

    private FurColor RandomFur()
    {
        var colors = Enum.GetValues<FurColor>();
        return colors[random.Next(colors.Length)];
    }
}
