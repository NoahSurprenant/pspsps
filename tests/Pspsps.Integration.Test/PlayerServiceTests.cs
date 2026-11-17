using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

public sealed class PlayerServiceTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task SignInWithLogin_CreatesThePlayerOnceAndFindsThemAfter()
    {
        var first = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "1001", "Mr. Whiskers!", CancellationToken.None);
        var second = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "1001", "renamed on discord", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.DisplayName, Is.EqualTo("Mr Whiskers"));
            Assert.That(first.IsGuest, Is.False);
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(second.DisplayName, Is.EqualTo("Mr Whiskers"));
        });
    }

    [Test]
    public async Task SignInWithLogin_TheSameKeyFromAnotherProviderIsAnotherPlayer()
    {
        var discord = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "2002", "Tom", CancellationToken.None);
        var google = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Google, "2002", "Tom", CancellationToken.None);

        Assert.That(google.Id, Is.Not.EqualTo(discord.Id));
    }

    [Test]
    public async Task CreateGuest_IsSavedWithAGeneratedName()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var found = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.That(guest.IsGuest, Is.True);
        Assert.That(guest.DisplayName, Does.Match(@"^Kitty \d{4}$"));
        Assert.That(found, Is.EqualTo(guest));
    }

    [Test]
    public async Task LinkLogin_KeepsTheGuestsIdAndCoinsAndMakesThemAnAccount()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 40);
        var key = NewKey();

        var linked = await Game.Get<IPlayerService>()
            .LinkLoginAsync(guest.Id, LoginProvider.Google, key, CancellationToken.None);
        var signedIn = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Google, key, "Someone Else", CancellationToken.None);
        var found = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(linked, Is.EqualTo(LinkLoginOutcome.Linked));
            Assert.That(found, Is.EqualTo(guest with { Coins = 40, IsGuest = false }));
            // Signing in with the login from anywhere now finds the same cat.
            Assert.That(signedIn, Is.EqualTo(found));
        });
    }

    [Test]
    public async Task LinkLogin_RefusesALoginAnotherPlayerHasAndChangesNothing()
    {
        var key = NewKey();
        var owner = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(guest.Id, LoginProvider.Discord, key, CancellationToken.None);
        var guestAfter = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);
        var ownerAfter = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.TakenByAnotherPlayer));
            Assert.That(guestAfter, Is.EqualTo(guest));
            Assert.That(ownerAfter, Is.EqualTo(owner));
        });
    }

    [Test]
    public async Task LinkLogin_ALoginThePlayerAlreadyHasIsLinked()
    {
        var key = NewKey();
        var player = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(player.Id, LoginProvider.Discord, key, CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.Linked));
    }

    [Test]
    public async Task LinkLogin_RefusesASecondLoginFromTheSameProvider()
    {
        var player = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, NewKey(), "Tom", CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(player.Id, LoginProvider.Discord, NewKey(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.Refused));
    }

    [Test]
    public async Task LinkLogin_ToAPlayerThatDoesNotExistIsNotFound()
    {
        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(Guid.NewGuid(), LoginProvider.Google, NewKey(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.PlayerNotFound));
    }

    [Test]
    public async Task LinkLogin_ALoginTakenWhileLinking_IsTakenAndNothingIsLeftToSave()
    {
        // Another browser signs in with the same account between the owner check and the save: the save hits the
        // login's primary key. The interceptor puts that sign-in exactly there.
        var key = NewKey();
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new SignInJustBeforeSaving(LoginProvider.Discord, key))
            .Options;
        await using var db = new PspspsDbContext(options);

        var result = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .LinkLoginAsync(guest.Id, LoginProvider.Discord, key, CancellationToken.None);
        var guestAfter = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);
        var owner = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Quick Cat", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.TakenByAnotherPlayer));
            Assert.That(db.ChangeTracker.HasChanges(), Is.False, "the refused login is still waiting to be saved");
            Assert.That(guestAfter, Is.EqualTo(guest));
            Assert.That(owner.Id, Is.Not.EqualTo(guest.Id));
        });
    }

    /// <summary>A provider user ID no other test uses: the tests share one database.</summary>
    internal static string NewKey() => Guid.NewGuid().ToString("N");

    internal static async Task GiveCoinsAsync(Guid playerId, int coins)
    {
        var db = Game.Get<IApplicationDbContext>();
        var player = await db.Players.SingleAsync(p => p.Id == playerId);
        player.EarnCoins(coins);
        await db.SaveChangesAsync();
    }

    internal static async Task DeleteAsync(Guid playerId)
    {
        var db = Game.Get<IApplicationDbContext>();
        db.Players.Remove(await db.Players.SingleAsync(p => p.Id == playerId));
        await db.SaveChangesAsync();
    }

    /// <summary>Signs in with a login in another context just before this one saves.</summary>
    private sealed class SignInJustBeforeSaving(LoginProvider provider, string key) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await Game.Get<IPlayerService>().SignInWithLoginAsync(provider, key, "Quick Cat", cancellationToken);
            return result;
        }
    }
}
