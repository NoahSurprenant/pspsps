using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;

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

        Assert.Multiple(() =>
        {
            Assert.That(linked.Outcome, Is.EqualTo(LinkLoginOutcome.Linked));
            Assert.That(linked.Player, Is.EqualTo(guest with { Coins = 40, IsGuest = false }));
            // Signing in with the login from anywhere now finds the same cat.
            Assert.That(signedIn, Is.EqualTo(linked.Player));
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
            Assert.That(result.Outcome, Is.EqualTo(LinkLoginOutcome.TakenByAnotherPlayer));
            Assert.That(result.Message, Is.EqualTo("That Discord account already has a cat."));
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

        Assert.That(result.Outcome, Is.EqualTo(LinkLoginOutcome.Linked));
        Assert.That(result.Player, Is.EqualTo(player));
    }

    [Test]
    public async Task LinkLogin_RefusesASecondLoginFromTheSameProvider()
    {
        var player = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, NewKey(), "Tom", CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(player.Id, LoginProvider.Discord, NewKey(), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(LinkLoginOutcome.Refused));
        Assert.That(result.Message, Is.EqualTo("This cat already has a Discord login."));
    }

    [Test]
    public async Task LinkLogin_ToAPlayerThatDoesNotExistIsNotFound()
    {
        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(Guid.NewGuid(), LoginProvider.Google, NewKey(), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(LinkLoginOutcome.PlayerNotFound));
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
}
