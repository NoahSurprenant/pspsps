using Pspsps.Domain.Fishing;
using Pspsps.Domain.Players;
using Pspsps.Domain.Shop;

namespace Pspsps.Domain.Test;

public sealed class ShopTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Catalogue_HasHatsCollarsAndEveryFur_WithUniqueIdsAndPositivePrices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ShopCatalog.All.Select(i => i.Id), Is.Unique);
            Assert.That(ShopCatalog.All.Select(i => i.Name), Is.Unique);
            Assert.That(ShopCatalog.All, Has.All.Property(nameof(ShopItem.Price)).GreaterThan(0));
            Assert.That(ShopCatalog.All.Count(i => i.Kind == ItemKind.Hat), Is.GreaterThanOrEqualTo(4));
            Assert.That(ShopCatalog.All.Count(i => i.Kind == ItemKind.Collar), Is.GreaterThanOrEqualTo(3));
            Assert.That(
                ShopCatalog.All.Where(i => i.Kind == ItemKind.Fur).Select(i => i.Fur),
                Is.EquivalentTo(Enum.GetValues<FurColor>().Cast<FurColor?>()));
            Assert.That(ShopCatalog.All.Where(i => i.Kind != ItemKind.Fur), Has.All.Property(nameof(ShopItem.Fur)).Null);
            Assert.That(ShopCatalog.All.Select(i => i.Id), Has.All.Length.LessThanOrEqualTo(32), "the database keeps 32 characters");
        });
    }

    [Test]
    public void Prices_AFirstHatAfterAFewCatches_TheCrownMuchLater()
    {
        var averageCatch = FishCatalog.All.Sum(f => (double)f.Coins * f.Weight) / FishCatalog.All.Sum(f => f.Weight);
        var hats = ShopCatalog.All.Where(i => i.Kind == ItemKind.Hat).ToList();

        Assert.That(hats.Min(h => h.Price) / averageCatch, Is.InRange(1.0, 4.0), "catches for the cheapest hat");
        Assert.That(ShopCatalog.Find("crown")!.Price / averageCatch, Is.GreaterThan(50), "catches for the crown");
        Assert.That(ShopCatalog.Find("crown")!.Price, Is.EqualTo(hats.Max(h => h.Price)));
    }

    [Test]
    public void ANewCat_OwnsTheCoatItWasBornWith_AndWearsNothingElse()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Calico, Now);

        Assert.Multiple(() =>
        {
            Assert.That(player.Items.Select(i => i.ItemId), Is.EqualTo(new[] { "fur-calico" }));
            Assert.That(player.Owns("fur-calico"), Is.True);
            Assert.That(player.Hat, Is.Null);
            Assert.That(player.Collar, Is.Null);
        });
    }

    [Test]
    public void Buy_PaysThePrice_AndThePlayerHasIt()
    {
        var player = WithCoins(100);

        var bought = player.Buy("party-hat", Now);

        Assert.Multiple(() =>
        {
            Assert.That(bought.Name, Is.EqualTo("Party hat"));
            Assert.That(player.Coins, Is.EqualTo(70));
            Assert.That(player.Owns("party-hat"), Is.True);
            Assert.That(player.Items.Single(i => i.ItemId == "party-hat").At, Is.EqualTo(Now));
            Assert.That(player.Hat, Is.Null, "buying doesn't put it on");
        });
    }

    [Test]
    public void Buy_WithExactlyThePrice_LeavesNoCoins()
    {
        var player = WithCoins(30);

        player.Buy("party-hat", Now);

        Assert.That(player.Coins, Is.Zero);
    }

    [Test]
    public void Buy_RefusesWhatThePlayerCantAfford_AndChangesNothing()
    {
        var player = WithCoins(29);

        var refused = Assert.Throws<DomainException>(() => player.Buy("party-hat", Now));

        Assert.That(refused?.Message, Is.EqualTo("The Party hat costs 30 coins. You have 29."));
        Assert.That(player.Coins, Is.EqualTo(29));
        Assert.That(player.Owns("party-hat"), Is.False);
    }

    [Test]
    public void Buy_RefusesWhatThePlayerHasAlready_AndChargesNothing()
    {
        var player = WithCoins(100);
        player.Buy("bow", Now);

        var twice = Assert.Throws<DomainException>(() => player.Buy("bow", Now));
        var bornWith = Assert.Throws<DomainException>(() => player.Buy("fur-grey", Now));

        Assert.That(twice?.Message, Is.EqualTo("You already have the Bow."));
        Assert.That(bornWith?.Message, Is.EqualTo("You already have the Grey fur."));
        Assert.That(player.Coins, Is.EqualTo(55));
        Assert.That(player.Items.Count(i => i.ItemId == "bow"), Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("hat")]
    [TestCase("PARTY-HAT")]
    public void Buy_RefusesWhatIsntInTheShop(string itemId)
    {
        var player = WithCoins(100);

        var refused = Assert.Throws<DomainException>(() => player.Buy(itemId, Now));

        Assert.That(refused?.Message, Is.EqualTo("That isn't in the shop."));
        Assert.That(player.Coins, Is.EqualTo(100));
    }

    [Test]
    public void Wear_PutsOnOneHatAndOneCollar_ANewOneReplacingTheOld()
    {
        var player = WithCoins(200);
        player.Buy("party-hat", Now);
        player.Buy("beanie", Now);
        player.Buy("red-collar", Now);
        player.Buy("bell-collar", Now);

        player.Wear("party-hat");
        player.Wear("red-collar");
        player.Wear("beanie");
        player.Wear("bell-collar");

        Assert.That((player.Hat, player.Collar), Is.EqualTo(("beanie", "bell-collar")));
    }

    [Test]
    public void Wear_AFur_ChangesTheCoat_AndTheOldOneCanBeWornAgain()
    {
        var player = WithCoins(200);
        player.Buy("fur-tuxedo", Now);

        player.Wear("fur-tuxedo");
        Assert.That(player.FurColor, Is.EqualTo(FurColor.Tuxedo));

        player.Wear("fur-grey");
        Assert.That(player.FurColor, Is.EqualTo(FurColor.Grey));
    }

    [Test]
    public void Wear_RefusesWhatThePlayerHasntBought()
    {
        var player = WithCoins(1000);

        var hat = Assert.Throws<DomainException>(() => player.Wear("crown"));
        var fur = Assert.Throws<DomainException>(() => player.Wear("fur-black"));
        var nothing = Assert.Throws<DomainException>(() => player.Wear("cape"));

        Assert.Multiple(() =>
        {
            Assert.That(hat?.Message, Is.EqualTo("Buy the Crown first."));
            Assert.That(fur?.Message, Is.EqualTo("Buy the Black fur first."));
            Assert.That(nothing?.Message, Is.EqualTo("That isn't in the shop."));
            Assert.That(player.Hat, Is.Null);
            Assert.That(player.FurColor, Is.EqualTo(FurColor.Grey));
        });
    }

    [Test]
    public void TakeOff_TakesOffTheHatOrCollarWorn_AndKeepsIt()
    {
        var player = WithCoins(100);
        player.Buy("bow", Now);
        player.Buy("blue-collar", Now);
        player.Wear("bow");
        player.Wear("blue-collar");

        player.TakeOff("bow");
        Assert.That((player.Hat, player.Collar), Is.EqualTo(((string?)null, "blue-collar")));
        player.TakeOff("blue-collar");
        Assert.That((player.Hat, player.Collar), Is.EqualTo(((string?)null, (string?)null)));
        Assert.That(player.Owns("bow") && player.Owns("blue-collar"), Is.True);
    }

    [Test]
    public void EveryChangeOfOutfit_CountsUp_SoARoomCanTellANewerOneFromAnOlder()
    {
        var player = WithCoins(100);
        player.Buy("bow", Now);
        Assert.That(player.OutfitVersion, Is.Zero, "buying puts nothing on");

        player.Wear("bow");
        player.Wear("fur-grey");
        player.TakeOff("bow");

        Assert.That(player.OutfitVersion, Is.EqualTo(3));
        Assert.Throws<DomainException>(() => player.TakeOff("bow"));
        Assert.That(player.OutfitVersion, Is.EqualTo(3), "a refused change counts nothing");
    }

    [Test]
    public void TakeOff_RefusesWhatIsntWorn_AndTheFur()
    {
        var player = WithCoins(100);
        player.Buy("bow", Now);

        var notWorn = Assert.Throws<DomainException>(() => player.TakeOff("bow"));
        var fur = Assert.Throws<DomainException>(() => player.TakeOff("fur-grey"));

        Assert.That(notWorn?.Message, Is.EqualTo("You aren't wearing that."));
        Assert.That(fur?.Message, Does.StartWith("A cat always has its fur."));
        Assert.That(player.FurColor, Is.EqualTo(FurColor.Grey));
    }

    [Test]
    public void FurOf_IsTheShopItemForEachColour_AndRefusesOneThatDoesntExist()
    {
        Assert.That(ShopCatalog.FurOf(FurColor.Cream).Id, Is.EqualTo("fur-cream"));
        Assert.Throws<DomainException>(() => ShopCatalog.FurOf((FurColor)99));
        Assert.Throws<DomainException>(() => Player.CreateGuest("Whiskers", (FurColor)99, Now));
    }

    private static Player WithCoins(int coins)
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);
        player.EarnCoins(coins);
        return player;
    }
}
