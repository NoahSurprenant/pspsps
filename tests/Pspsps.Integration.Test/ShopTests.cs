using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Friends;
using Pspsps.Application.Players;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Players;
using Pspsps.Domain.Shop;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Game;
using static Pspsps.Integration.Test.PlayerServiceTests;

namespace Pspsps.Integration.Test;

/// <summary>The shop against PostgreSQL: purchases saved, coins never overspent, and what a cat wears shown live.</summary>
public sealed class ShopTests
{
    private static GameFactory Game => PostgresFixture.Game;

    private static IShopService Shop => Game.Get<IShopService>();

    [Test]
    public async Task Buy_ChargesTheCoins_AndThePlayerHasItFromThenOn()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 100);

        var bought = await Shop.BuyAsync(guest.Id, "party-hat", CancellationToken.None);
        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(bought.Coins, Is.EqualTo(70));
            Assert.That(saved.Coins, Is.EqualTo(70));
            Assert.That(saved.Owned, Is.EquivalentTo(new[] { ShopCatalog.FurOf(guest.FurColor).Id, ShopCatalog.StarterFurniture, "party-hat" }),
                "a guest buys like anyone, and has the coat and the bed it was born with");
            Assert.That(saved.Hat, Is.Null);
        });
    }

    [Test]
    public async Task Wear_AndTakeOff_AreSaved_AndTheProfileSaysWhatTheCatWears()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 500);
        await Shop.BuyAsync(guest.Id, "top-hat", CancellationToken.None);
        await Shop.BuyAsync(guest.Id, "green-collar", CancellationToken.None);
        await Shop.BuyAsync(guest.Id, "fur-tuxedo", CancellationToken.None);

        await Shop.WearAsync(guest.Id, "top-hat", CancellationToken.None);
        await Shop.WearAsync(guest.Id, "green-collar", CancellationToken.None);
        await Shop.WearAsync(guest.Id, "fur-tuxedo", CancellationToken.None);
        await Shop.TakeOffAsync(guest.Id, "green-collar", CancellationToken.None);
        var profile = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.That(profile, Is.EqualTo(guest with
        {
            Coins = 500 - 250 - 20 - 150, FurColor = FurColor.Tuxedo, Hat = "top-hat", Collar = null, OutfitVersion = 4,
        }));
    }

    [Test]
    public async Task Buy_RefusedRules_ChangeNothing()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 40);
        await Shop.BuyAsync(guest.Id, "red-collar", CancellationToken.None);

        var poor = Assert.ThrowsAsync<DomainException>(() => Shop.BuyAsync(guest.Id, "beanie", CancellationToken.None));
        var twice = Assert.ThrowsAsync<DomainException>(() => Shop.BuyAsync(guest.Id, "red-collar", CancellationToken.None));
        var unknown = Assert.ThrowsAsync<DomainException>(() => Shop.WearAsync(Guid.NewGuid(), "red-collar", CancellationToken.None));

        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(poor!.Message, Is.EqualTo("The Beanie costs 60 coins. You have 20."));
            Assert.That(twice!.Message, Is.EqualTo("You already have the Red collar."));
            Assert.That(unknown!.Message, Is.EqualTo("Your cat wasn't found. Sign in again."));
            Assert.That(saved.Coins, Is.EqualTo(20));
            Assert.That(saved.Owned, Has.Count.EqualTo(3), "its coat, its bed and the red collar");
        });
    }

    [Test]
    public async Task TwoPurchasesAtOnce_NeverSpendCoinsThatAreGone()
    {
        // 100 coins; another tab buys the beanie (60) just before this purchase of the bow (45) saves: both read 100.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 100);
        await using var db = RacingContext(ct => Shop.BuyAsync(guest.Id, "beanie", ct));

        var refused = Assert.ThrowsAsync<DomainException>(() =>
            new ShopService(db, TimeProvider.System).BuyAsync(guest.Id, "bow", CancellationToken.None));
        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Message, Is.EqualTo("The Bow costs 45 coins. You have 40."), "decided again on the coins left");
            Assert.That(saved.Coins, Is.EqualTo(40));
            Assert.That(saved.Owned, Does.Contain("beanie").And.Not.Contain("bow"));
        });
    }

    [Test]
    public async Task TheSameThingBoughtTwiceAtOnce_IsBoughtAndPaidForOnce()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 100);
        await using var db = RacingContext(ct => Shop.BuyAsync(guest.Id, "bow", ct));

        var refused = Assert.ThrowsAsync<DomainException>(() =>
            new ShopService(db, TimeProvider.System).BuyAsync(guest.Id, "bow", CancellationToken.None));
        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);

        Assert.That(refused!.Message, Is.EqualTo("You already have the Bow."));
        Assert.That(saved.Coins, Is.EqualTo(55));
    }

    [Test]
    public async Task APurchaseAndACatchAtOnce_BothCount()
    {
        // A catch is paid (coins = coins + 20, in one statement) just before the purchase saves.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 50);
        await using var db = RacingContext(ct => Game.Get<IPlayerService>().EarnCoinsAsync(guest.Id, 20, ct));

        var bought = await new ShopService(db, TimeProvider.System).BuyAsync(guest.Id, "bow", CancellationToken.None);
        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);

        Assert.That(bought.Coins, Is.EqualTo(25));
        Assert.That(saved.Coins, Is.EqualTo(25), "the purchase saved over the catch");
    }

    [Test]
    public async Task ManyPurchasesAtOnce_SpendAtMostWhatThePlayerHad()
    {
        // Every hat and collar at once (1475 coins of them) with 200 coins: whatever gets through, it is paid for, in full,
        // and the coins never go below zero.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 200);
        var items = ShopCatalog.All.Where(i => i.Kind is ItemKind.Hat or ItemKind.Collar).ToList();

        var results = await Task.WhenAll(items.Select(async item =>
        {
            try
            {
                await Shop.BuyAsync(guest.Id, item.Id, CancellationToken.None);
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }));
        var saved = await Shop.GetAsync(guest.Id, CancellationToken.None);
        var spent = items.Where(i => saved.Owned.Contains(i.Id)).Sum(i => i.Price);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Coins, Is.GreaterThanOrEqualTo(0));
            Assert.That(saved.Coins, Is.EqualTo(200 - spent), "every item had is paid for, and nothing else");
            Assert.That(results.Count(r => r), Is.EqualTo(items.Count(i => saved.Owned.Contains(i.Id))));
            Assert.That(results, Does.Contain(true), "something was bought");
        });
    }

    [Test]
    public async Task ShowingPlayers_DoesntReadWhatTheyOwn()
    {
        // Joining a room, signing in, a card and the friends list need a cat's name, fur and what it wears: never the
        // rows of everything it has (logins times items, for every friend in a list).
        var key = NewKey();
        var tom = await Game.Get<IPlayerService>().SignInWithLoginAsync(
            LoginProvider.Discord, key, "Tom", CancellationToken.None);
        var felix = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await Game.Get<IFriendService>().RequestAsync(tom.Id, felix.Id, CancellationToken.None);
        var statements = new ConcurrentQueue<string>();
        await using var db = new PspspsDbContext(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new Statements(statements))
            .Options);
        var players = new PlayerService(db, TimeProvider.System, Random.Shared);
        var friends = new FriendService(db, TimeProvider.System);

        await players.FindAsync(tom.Id, CancellationToken.None);
        await players.SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);
        await friends.GetAsync(tom.Id, felix.Id, CancellationToken.None);
        await friends.ListAsync(tom.Id, CancellationToken.None);

        Assert.That(statements, Is.Not.Empty);
        Assert.That(statements, Has.None.Contains("player_items"));
    }

    private sealed class Statements(ConcurrentQueue<string> seen) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            seen.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task TheDatabase_RefusesCoinsBelowZero()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var db = Game.Get<IApplicationDbContext>();

        Assert.ThrowsAsync(Is.InstanceOf<Exception>(), () => db.Players
            .Where(p => p.Id == guest.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Coins, -1)));

        Assert.That((await Shop.GetAsync(guest.Id, CancellationToken.None)).Coins, Is.Zero);
    }

    [Test]
    public async Task WearingAHat_ShowsOnTheCatForEveryoneInTheRoom_AndForCatsArrivingLater()
    {
        var (tom, cookie) = await Game.SignInGuestAsync();
        await GiveCoinsAsync(tom.Id, 30);
        await using var tomHub = await Game.ConnectAsync(cookie);
        var (_, felixHub) = await Game.ConnectGuestAsync();
        await using var felixConnection = felixHub;
        var felixSees = new Recorder(felixHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var shop = await tomHub.InvokeAsync<ShopView>("Buy", "party-hat");
        Assert.That((shop.Coins, shop.Hat), Is.EqualTo((0, (string?)null)));
        var worn = await tomHub.InvokeAsync<ShopView>("Wear", "party-hat");
        Assert.That(worn.Hat, Is.EqualTo("party-hat"));

        var dressed = await felixSees.Next<OutfitView>("CatDressed", o => o.PlayerId == tom.Id);
        Assert.That(dressed, Is.EqualTo(new OutfitView(tom.Id, tom.FurColor.ToString(), "party-hat", null, Version: 1)));

        // A cat arriving later sees it in the room's snapshot.
        var (_, garfieldHub) = await Game.ConnectGuestAsync();
        await using var garfieldConnection = garfieldHub;
        var snapshot = await garfieldHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        Assert.That(snapshot.Cats.Single(c => c.PlayerId == tom.Id).Hat, Is.EqualTo("party-hat"));

        // And Tom's card says so.
        var card = await garfieldHub.InvokeAsync<PlayerCardView>("GetPlayerCard", tom.Id);
        Assert.That(card.HatName, Is.EqualTo("Party hat"));

        // Taken off, live too.
        await tomHub.InvokeAsync<ShopView>("TakeOff", "party-hat");
        var bare = await felixSees.Next<OutfitView>("CatDressed", o => o.PlayerId == tom.Id);
        Assert.That(bare.Hat, Is.Null);
    }

    [Test]
    public async Task Buying_WithoutTheCoins_IsThePlayersMessage()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var connection = hub;

        var refused = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<ShopView>("Buy", "crown"));

        Assert.That(refused!.Message, Does.Contain("The Crown costs 1000 coins. You have 0."));
        var shop = await hub.InvokeAsync<ShopView>("GetShop");
        Assert.That(shop.Items, Has.Count.EqualTo(ShopCatalog.All.Count));
    }
}
