using Pspsps.Domain.Players;

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
    }

    [Test]
    public void ChangeFurColor_RefusesAColorThatDoesNotExist()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        Assert.Throws<DomainException>(() => player.ChangeFurColor((FurColor)99));
        player.ChangeFurColor(FurColor.Tuxedo);
        Assert.That(player.FurColor, Is.EqualTo(FurColor.Tuxedo));
    }

    [Test]
    public void Rename_AppliesTheNameRules()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        player.Rename("  Sir   Fluff ");
        Assert.That(player.DisplayName, Is.EqualTo("Sir Fluff"));
        Assert.Throws<DomainException>(() => player.Rename("<script>"));
    }
}
