using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Friends;
using Pspsps.Application.Homes;
using Pspsps.Application.Players;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Game;
using static Pspsps.Integration.Test.PlayerServiceTests;

namespace Pspsps.Integration.Test;

/// <summary>Players' homes against PostgreSQL and the real hub: furniture saved, who may visit, and who is sent out.</summary>
public sealed class HomesTests
{
    private static GameFactory Game => PostgresFixture.Game;

    private static IHomeService Homes => Game.Get<IHomeService>();

    [Test]
    public async Task Furniture_PutOutTurnedAndPutAway_IsSaved()
    {
        var tom = await NewPlayerAsync();

        var placed = await Homes.PlaceAsync(tom, ShopCatalog.StarterFurniture, 2, -1, 1, CancellationToken.None);
        var piece = placed.Furniture.Single();
        await Homes.TurnAsync(tom, piece.Id, CancellationToken.None);
        var saved = await Homes.GetAsync(tom, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Furniture.Select(p => (p.Id, p.ItemId, p.X, p.Z, p.QuarterTurns)),
                Is.EqualTo(new[] { (piece.Id, ShopCatalog.StarterFurniture, 2.0, -1.0, 2) }));
            Assert.That(saved.Visitors, Is.EqualTo(HomeVisitors.Friends));
            Assert.That(saved.Version, Is.EqualTo(2));
        });

        await Homes.PutAwayAsync(tom, piece.Id, CancellationToken.None);
        Assert.That((await Homes.GetAsync(tom, CancellationToken.None)).Furniture, Is.Empty);
    }

    [Test]
    public async Task Furniture_BoughtTwice_GoesOutTwice_AndNoMore()
    {
        var tom = await NewPlayerAsync();
        await GiveCoinsAsync(tom, 100);
        var shop = Game.Get<IShopService>();
        await shop.BuyAsync(tom, "plant", CancellationToken.None);
        var wardrobe = await shop.BuyAsync(tom, "plant", CancellationToken.None);

        await Homes.PlaceAsync(tom, "plant", null, null, 0, CancellationToken.None);
        await Homes.PlaceAsync(tom, "plant", null, null, 0, CancellationToken.None);
        var third = Assert.ThrowsAsync<DomainException>(() => Homes.PlaceAsync(tom, "plant", null, null, 0, CancellationToken.None));
        var notOwned = Assert.ThrowsAsync<DomainException>(() => Homes.PlaceAsync(tom, "sofa", null, null, 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(wardrobe.Counts["plant"], Is.EqualTo(2));
            Assert.That(wardrobe.Coins, Is.EqualTo(30));
            Assert.That(third!.Message, Is.EqualTo("Every Potted plant you have is out already."));
            Assert.That(notOwned!.Message, Is.EqualTo("Buy the Sofa first."));
        });
        Assert.That((await Homes.GetAsync(tom, CancellationToken.None)).Furniture, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TwoPiecesPutOutAtOnce_OnTheSameSpot_OnlyOneStandsThere()
    {
        // Two tabs put out a piece at the same spot: the second, decided on the home before the first saved, is decided
        // again on the home as it is now, and refused.
        var tom = await NewPlayerAsync();
        await GiveCoinsAsync(tom, 100);
        await Game.Get<IShopService>().BuyAsync(tom, "plant", CancellationToken.None);
        await Homes.PlaceAsync(tom, "plant", -3, -3, 0, CancellationToken.None); // the home exists before the race
        await Homes.PutAwayAsync(tom, (await Homes.GetAsync(tom, CancellationToken.None)).Furniture.Single().Id, CancellationToken.None);
        await using var db = RacingContext(ct => Homes.PlaceAsync(tom, "plant", 2, -1, 0, ct));

        var refused = Assert.ThrowsAsync<DomainException>(() =>
            new HomeService(db, TimeProvider.System).PlaceAsync(tom, ShopCatalog.StarterFurniture, 2.2, -1, 0, CancellationToken.None));
        var saved = await Homes.GetAsync(tom, CancellationToken.None);

        Assert.That(refused!.Message, Is.EqualTo("There's something in the way there."));
        Assert.That(saved.Furniture.Select(p => p.ItemId), Is.EqualTo(new[] { "plant" }));
    }

    [Test]
    public async Task TheFirstChangeToAHome_FromTwoTabsAtOnce_MakesOneHome_WithBoth()
    {
        var tom = await NewPlayerAsync();
        await using var db = RacingContext(ct => Homes.LetInAsync(tom, HomeVisitors.Anyone, ct));

        await new HomeService(db, TimeProvider.System).PlaceAsync(tom, ShopCatalog.StarterFurniture, 2, -1, 0, CancellationToken.None);
        var saved = await Homes.GetAsync(tom, CancellationToken.None);

        Assert.That(saved.Visitors, Is.EqualTo(HomeVisitors.Anyone));
        Assert.That(saved.Furniture, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task VisitHome_OnlyFriends_UntilTheOwnerLetsAnyoneIn_OrNobody()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");

        var stranger = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id));
        Assert.That(stranger!.Message, Does.Contain("Only friends can visit that home."));
        Assert.That((await felixHub.InvokeAsync<PlayerCardView>("GetPlayerCard", tom.Id)).CanVisitHome, Is.False);

        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Anyone");
        Assert.That((await felixHub.InvokeAsync<PlayerCardView>("GetPlayerCard", tom.Id)).CanVisitHome, Is.True);
        var there = await felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id);

        Assert.Multiple(() =>
        {
            Assert.That(there.RoomName, Is.EqualTo($"{tom.DisplayName}'s home"));
            Assert.That(there.WorldId, Is.EqualTo("calico"), "a visitor is still in the world it came from");
            Assert.That(there.Home?.OwnerId, Is.EqualTo(tom.Id));
            Assert.That(there.Pond, Is.Null);
            Assert.That(there.Doors.Single().ToRoomId, Is.EqualTo("town-square"));
            Assert.That(there.Cats.Single(c => c.PlayerId == felix.Id).X, Is.EqualTo(Home.Layout.Spawn.X), "arrives by the door");
        });

        await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "town-square");
        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Nobody");
        var closed = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id));
        Assert.That(closed!.Message, Does.Contain("That home isn't open to visitors right now."));
        var home = await tomHub.InvokeAsync<RoomSnapshot>("GoHome");
        Assert.That(home.Home?.Visitors, Is.EqualTo("Nobody"), "the owner always comes home");
    }

    [Test]
    public async Task AStricterSetting_SendsOutWhoMayNoLongerStay_BackToTheirTownSquare()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        var (garfield, garfieldHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await using var ___ = garfieldHub;
        await BefriendAsync(tom.Id, garfield.Id);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        await tomHub.InvokeAsync<RoomSnapshot>("GoHome");
        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Anyone");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await garfieldHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id);
        await garfieldHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id);
        var felixOut = SentOut(felixHub, tom.Id);
        var garfieldOut = SentOut(garfieldHub, tom.Id);
        var tomSees = new Recorder(tomHub);
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        var homeGrain = grains.GetGrain<IRoomGrain>(RoomKey.ForHome(tom.Id));

        // Friends only: Felix, not a friend, is sent out; Garfield, a friend, stays.
        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Friends");

        Assert.That(await felixOut.Task.WaitAsync(TimeSpan.FromSeconds(10)), Does.Contain("changed who can visit their home"));
        Assert.That(await tomSees.Next<Guid>("CatLeft", id => id == felix.Id), Is.EqualTo(felix.Id));
        Assert.That(await homeGrain.CatsHere(), Is.EquivalentTo(new[] { tom.Id, garfield.Id }));
        Assert.That(garfieldOut.Task.IsCompleted, Is.False);
        // Felix goes back to Town Square, in his own world.
        var back = await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "town-square");
        Assert.That((back.WorldId, back.RoomId), Is.EqualTo(("tabby", "town-square")));

        // Nobody: Garfield goes too; Tom, the owner, stays.
        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Nobody");
        await garfieldOut.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(await homeGrain.CatsHere(), Is.EqualTo(new[] { tom.Id }));
    }

    [Test]
    public async Task AVisitor_SeesTheOwnersChanges_AndLeavesByTheDoor_ToTheirTownSquare()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await BefriendAsync(tom.Id, felix.Id);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id);
        var changed = new TaskCompletionSource<HomeView>();
        felixHub.On<HomeView>("HomeChanged", home => changed.TrySetResult(home));

        // Tom, in Town Square, puts his bed out: Felix, in Tom's home, sees it.
        var placed = await tomHub.InvokeAsync<HomeView>("PlaceFurniture", ShopCatalog.StarterFurniture, 2.0, -1.0, 0);
        var seen = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(seen.Furniture.Select(p => p.Id), Is.EqualTo(placed.Furniture.Select(p => p.Id)));
        Assert.That(seen.Props().Single().Kind, Is.EqualTo(ShopCatalog.StarterFurniture));

        // Felix's friends list says where Tom isn't; Tom's says Felix is in Tom's home.
        var felixInList = (await tomHub.InvokeAsync<List<FriendView>>("GetFriends")).Single();
        Assert.That(felixInList.Place, Is.EqualTo(new FriendPlaceView("", "", "home", "Your home", tom.Id)));
        Assert.That(felixInList.CanVisitHome, Is.True);

        // Felix walks onto the door and goes through: Town Square of Calico, the world he came from, at its spawn point.
        var door = Home.Layout.GetDoor(Home.DoorOut).Spot;
        await felixHub.InvokeAsync("Move", door.X, door.Z);
        var square = await felixHub.InvokeAsync<RoomSnapshot>("GoThroughDoor", Home.DoorOut);
        Assert.That((square.WorldId, square.RoomId, square.Home), Is.EqualTo(("calico", "town-square", (HomeView?)null)));
        var spawn = RoomCatalog.Default.Spawn;
        Assert.That(square.Cats.Single(c => c.PlayerId == felix.Id), Has.Property(nameof(CatView.X)).EqualTo(spawn.X)
            .And.Property(nameof(CatView.Z)).EqualTo(spawn.Z));
    }

    [Test]
    public async Task GoToFriend_InAHome_VisitsThatHome_IfItLetsThePlayerIn()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await BefriendAsync(tom.Id, felix.Id);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await tomHub.InvokeAsync<RoomSnapshot>("GoHome");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");

        var friend = (await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single();
        Assert.That(friend.Place, Is.EqualTo(new FriendPlaceView("", "", "home", $"{tom.DisplayName}'s home", tom.Id)));
        var there = await felixHub.InvokeAsync<RoomSnapshot>("GoToFriend", tom.Id);
        Assert.That(there.Home?.OwnerId, Is.EqualTo(tom.Id));
        Assert.That(there.Cats.Select(c => c.PlayerId), Is.SupersetOf(new[] { tom.Id, felix.Id }));

        await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "town-square");
        await tomHub.InvokeAsync<HomeView>("LetIntoHome", "Nobody");
        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("GoToFriend", tom.Id));
        Assert.That(refused!.Message, Does.Contain("That home isn't open to visitors right now."));
    }

    [Test]
    public async Task AHomeAlreadyOpen_IsCalledAfterItsOwnersNameAsItIsNow()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await BefriendAsync(tom.Id, felix.Id);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await tomHub.InvokeAsync<RoomSnapshot>("GoHome");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");

        // A change told to the home, as the owner is called now (renamed since it opened).
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        await grains.GetGrain<IRoomGrain>(RoomKey.ForHome(tom.Id)).Furnish(new HomeView(tom.Id, "Tommy", "Friends", [], 1));
        var there = await felixHub.InvokeAsync<RoomSnapshot>("VisitHome", tom.Id);

        Assert.That(there.RoomName, Is.EqualTo("Tommy's home"));
    }

    [Test]
    public async Task WhoMayVisit_IsDecidedWithoutReadingTheFurniture()
    {
        // Every card, friends list, visit and send-out asks who may come in: never the rows of every piece in the home.
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        await BefriendAsync(tom, felix);
        await Homes.PlaceAsync(tom, ShopCatalog.StarterFurniture, 2, -1, 0, CancellationToken.None);
        var statements = new ConcurrentQueue<string>();
        await using var db = new PspspsDbContext(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new ShopTests.Statements(statements))
            .Options);
        var homes = new HomeService(db, TimeProvider.System);

        var open = await homes.OpenToAsync(felix, [tom], CancellationToken.None);
        await homes.VisitAsync(felix, tom, CancellationToken.None);
        var leaving = await homes.MustLeaveAsync(tom, [felix], CancellationToken.None);

        Assert.That(open, Is.EquivalentTo(new[] { tom }));
        Assert.That(leaving, Is.Empty);
        Assert.That(statements, Is.Not.Empty);
        Assert.That(statements, Has.None.Contains("home_furniture"));
    }

    // The message the hub sends when a cat is sent out of a home.
    private static TaskCompletionSource<string> SentOut(HubConnection hub, Guid ownerId)
    {
        var sent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Which home it was, so a browser that has gone into another meanwhile stays there.
        hub.On<Guid, string>("SentOut", (owner, message) =>
        {
            if (owner == ownerId)
            {
                sent.TrySetResult(message);
            }
        });
        return sent;
    }

    private static async Task<Guid> NewPlayerAsync() =>
        (await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None)).Id;

    private static async Task BefriendAsync(Guid one, Guid other)
    {
        await Game.Get<IFriendService>().RequestAsync(one, other, CancellationToken.None);
        await Game.Get<IFriendService>().AcceptAsync(other, one, CancellationToken.None);
    }
}
