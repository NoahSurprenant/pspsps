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
}
