using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class PlayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void CreateGuest_StartsWithNoCoinsAndNoLogins()
    {
        var player = Player.CreateGuest("Kitty 1234", FurColor.Calico, Now);

        Assert.Multiple(() =>
        {
            Assert.That(player.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(player.DisplayName, Is.EqualTo("Kitty 1234"));
            Assert.That(player.FurColor, Is.EqualTo(FurColor.Calico));
            Assert.That(player.Coins, Is.Zero);
            Assert.That(player.IsGuest, Is.True);
            Assert.That(player.Logins, Is.Empty);
            Assert.That(player.CreatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void CreateWithLogin_KeepsTheLogin()
    {
        var player = Player.CreateWithLogin(LoginProvider.Discord, "80351110224678912", "Whiskers", FurColor.Black, Now);

        Assert.That(player.IsGuest, Is.False);
        Assert.That(player.Logins.Single(), Has.Property(nameof(PlayerLogin.Provider)).EqualTo(LoginProvider.Discord)
            .And.Property(nameof(PlayerLogin.ProviderKey)).EqualTo("80351110224678912"));
    }

    [Test]
    public void CreateWithLogin_RefusesAnEmptyProviderKey() =>
        Assert.Throws<DomainException>(() =>
            Player.CreateWithLogin(LoginProvider.Google, " ", "Whiskers", FurColor.Black, Now));

    [Test]
    public void LinkLogin_TurnsAGuestIntoAnAccountAndKeepsTheCat()
    {
        var player = Player.CreateGuest("Kitty 1234", FurColor.Calico, Now);
        player.EarnCoins(75);
        var id = player.Id;

        player.LinkLogin(LoginProvider.Google, "109876543210");

        Assert.Multiple(() =>
        {
            Assert.That(player.IsGuest, Is.False);
            Assert.That(player.Id, Is.EqualTo(id));
            Assert.That(player.DisplayName, Is.EqualTo("Kitty 1234"));
            Assert.That(player.FurColor, Is.EqualTo(FurColor.Calico));
            Assert.That(player.Coins, Is.EqualTo(75));
            Assert.That(player.CreatedAt, Is.EqualTo(Now));
            Assert.That(player.Logins.Single(), Has.Property(nameof(PlayerLogin.Provider)).EqualTo(LoginProvider.Google)
                .And.Property(nameof(PlayerLogin.ProviderKey)).EqualTo("109876543210"));
        });
    }

    [Test]
    public void LinkLogin_AddsAnotherProvidersLogin()
    {
        var player = Player.CreateWithLogin(LoginProvider.Discord, "80351110224678912", "Whiskers", FurColor.Black, Now);

        player.LinkLogin(LoginProvider.Google, "109876543210");

        Assert.That(
            player.Logins.Select(l => l.Provider), Is.EquivalentTo(new[] { LoginProvider.Discord, LoginProvider.Google }));
        Assert.That(player.IsGuest, Is.False);
    }

    [Test]
    public void LinkLogin_RefusesALoginThePlayerAlreadyHas()
    {
        var player = Player.CreateWithLogin(LoginProvider.Discord, "80351110224678912", "Whiskers", FurColor.Black, Now);

        var refused = Assert.Throws<DomainException>(() => player.LinkLogin(LoginProvider.Discord, "80351110224678912"));

        Assert.That(refused?.Message, Does.Contain("already has a Discord login"));
        Assert.That(player.Logins, Has.Count.EqualTo(1));
    }

    [Test]
    public void LinkLogin_RefusesASecondLoginFromTheSameProvider()
    {
        var player = Player.CreateWithLogin(LoginProvider.Discord, "80351110224678912", "Whiskers", FurColor.Black, Now);

        var refused = Assert.Throws<DomainException>(() => player.LinkLogin(LoginProvider.Discord, "41771983423143937"));

        Assert.That(refused?.Message, Does.Contain("already has a Discord login"));
        Assert.That(player.Logins, Has.Count.EqualTo(1));
    }

    [Test]
    public void HasLoginFrom_IsPerProvider()
    {
        var player = Player.CreateWithLogin(LoginProvider.Discord, "80351110224678912", "Whiskers", FurColor.Black, Now);

        Assert.That(player.HasLoginFrom(LoginProvider.Discord), Is.True);
        Assert.That(player.HasLoginFrom(LoginProvider.Google), Is.False);
    }

    [Test]
    public void LinkLogin_RefusesAnEmptyProviderKeyAndStaysAGuest()
    {
        var player = Player.CreateGuest("Kitty 1234", FurColor.Calico, Now);

        Assert.Throws<DomainException>(() => player.LinkLogin(LoginProvider.Google, " "));
        Assert.Throws<DomainException>(() => player.LinkLogin((LoginProvider)99, "109876543210"));

        Assert.That(player.IsGuest, Is.True);
        Assert.That(player.Logins, Is.Empty);
    }

    [Test]
    public void SpendCoins_RefusesMoreThanThePlayerHas()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);
        player.EarnCoins(50);

        Assert.Throws<DomainException>(() => player.SpendCoins(51));
        player.SpendCoins(50);
        Assert.That(player.Coins, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Coins_MustMoveByAPositiveAmount(int amount)
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        Assert.Throws<DomainException>(() => player.EarnCoins(amount));
        Assert.Throws<DomainException>(() => player.SpendCoins(amount));
        Assert.Throws<DomainException>(() => Player.Earning(amount));
    }

    [Test]
    public void Earning_IsTheIncrementThePlayersCoinsGoUpBy()
    {
        // What the database adds in one statement (no read, then write, that two earnings could race).
        Assert.That(Player.Earning(40), Is.EqualTo(40));
    }

    [Test]
    public void Rename_AppliesTheNameRules()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        player.Rename("  Sir   Fluff ", ChatFilter.None, Now);
        Assert.That(player.DisplayName, Is.EqualTo("Sir Fluff"));
        Assert.Throws<DomainException>(() => player.Rename("<script>", ChatFilter.None, Now));
    }
}
