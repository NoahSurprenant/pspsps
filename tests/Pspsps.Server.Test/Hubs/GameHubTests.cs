using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pspsps.Application.Friends;
using Pspsps.Application.Homes;
using Pspsps.Application.Players;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;
using Pspsps.Domain.Shop;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Test.Hubs;

/// <summary>The hub's bookkeeping of where a connection is, against substitute grains.</summary>
public sealed class GameHubTests
{
    private const string Connection = "tom-tab";
    private static readonly PlayerProfile Tom = new(Guid.NewGuid(), "Tom", FurColor.Grey, 0, IsGuest: true);
    private static readonly Guid Felix = Guid.NewGuid();

    private ServiceProvider _services = null!;
    private IGrainFactory _grains = null!;
    private IWorldGrain _world = null!;
    private IRoomGrain _room = null!;
    private IPlayerService _players = null!;
    private IFriendService _friends = null!;
    private IShopService _shop = null!;
    private IHomeService _homes = null!;
    private IPlayerSessionGrain _session = null!;
    private IRoomGrain _cafe = null!;
    private IHubCallerClients<IGameClient> _clients = null!;
    private SessionHeartbeat _heartbeat = null!;

    [SetUp]
    public void Substitutes()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _grains = Substitute.For<IGrainFactory>();
        _world = Substitute.For<IWorldGrain>();
        _room = Substitute.For<IRoomGrain>();
        var session = _session = Substitute.For<IPlayerSessionGrain>();
        _cafe = Substitute.For<IRoomGrain>();
        _grains.GetGrain<IRoomGrain>("tabby/cafe", null).Returns(_cafe);
        _grains.GetGrain<IWorldGrain>("tabby", null).Returns(_world);
        _grains.GetGrain<IRoomGrain>("tabby/town-square", null).Returns(_room);
        _grains.GetGrain<IPlayerSessionGrain>(Tom.Id, null).Returns(session);
        session.Claim(Connection).Returns((string?)null);
        session.IsCurrent(Connection).Returns(true);
        _world.TryEnter(Tom.Id, Connection).Returns(true);
        _players = Substitute.For<IPlayerService>();
        _friends = Substitute.For<IFriendService>();
        _shop = Substitute.For<IShopService>();
        _homes = Substitute.For<IHomeService>();
        _players.FindAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(Tom);
        _heartbeat = new SessionHeartbeat(
            _grains,
            Substitute.For<IHubContext<GameHub, IGameClient>>(),
            new GameMetrics(_services.GetRequiredService<IMeterFactory>()),
            TimeProvider.System,
            NullLogger<SessionHeartbeat>.Instance);
    }

    [TearDown]
    public void Dispose()
    {
        _heartbeat.Dispose();
        _services.Dispose();
    }

    [Test]
    public async Task OnDisconnected_WhenLeavingTheRoomFails_StillLeavesTheWorld_AndStopsRenewing()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.Leave(Tom.Id, Connection).ThrowsAsync(new TimeoutException());

        try
        {
            await hub.OnDisconnectedAsync(null);
        }
        catch (TimeoutException)
        {
            // Whether the failure surfaces doesn't matter; what the hub does despite it does.
        }

        await _world.Received(1).Leave(Tom.Id, Connection);
        await _heartbeat.RenewAsync();
        await _world.DidNotReceiveWithAnyArgs().Renew(default!);
        await _room.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task EnterWorld_FinishingAfterTheConnectionClosed_IsNotRenewed()
    {
        var admitted = new TaskCompletionSource<bool>();
        _world.TryEnter(Tom.Id, Connection).Returns(admitted.Task);
        var hub = Hub();

        // The tab closes while the world is still answering; the disconnect runs first, then entering finishes.
        var entering = hub.EnterWorld("tabby");
        await hub.OnDisconnectedAsync(null);
        admitted.SetResult(true);
        try
        {
            await entering;
        }
        catch (HubException)
        {
            // Refusing to finish is fine too.
        }

        await _world.Received(1).Leave(Tom.Id, Connection);
        await _heartbeat.RenewAsync();
        await _world.DidNotReceiveWithAnyArgs().Renew(default!);
        await _room.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task EnterWorld_WhenTheTabClosedBeforeItsClaimLanded_DoesNotReplaceTheLiveTab()
    {
        // A refresh: the old page's connection closes while it is still entering; the new page is the live tab.
        var claimed = new TaskCompletionSource<string?>();
        _session.Claim(Connection).Returns(claimed.Task);
        var hub = Hub();

        var entering = hub.EnterWorld("tabby");
        await hub.OnDisconnectedAsync(null);
        claimed.SetResult("live-tab");
        try
        {
            await entering;
        }
        catch (HubException)
        {
            // Refusing to finish is the point.
        }

        await _clients.Client("live-tab").DidNotReceive().SessionReplaced();
        // Released by the disconnect, and again after the late claim: the dead connection isn't the current tab.
        await _session.Received(2).Release(Connection);
        await _world.DidNotReceiveWithAnyArgs().TryEnter(default, default!);
    }

    [Test]
    public async Task GoToRoom_WhenTheTabClosedMeanwhile_DoesNotJoinTheNextRoom()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        var current = new TaskCompletionSource<bool>();
        _session.IsCurrent(Connection).Returns(current.Task);

        var going = hub.GoToRoom("cafe");
        await hub.OnDisconnectedAsync(null);
        current.SetResult(true);
        try
        {
            await going;
        }
        catch (HubException)
        {
            // Refusing to finish is the point.
        }

        await _cafe.DidNotReceiveWithAnyArgs().Join(default!, default!, default);
    }

    [Test]
    public async Task GoThroughDoor_JoinsTheRoomItLeadsTo_ByTheMatchingDoor_AndRenewsThere()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe").Returns(new DoorTarget("cafe", "town-square"));
        RenewalsFindEveryCat();

        await hub.GoThroughDoor("cafe");

        await _cafe.Received(1).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, "town-square");
        await hub.Groups.Received(1).RemoveFromGroupAsync(Connection, "tabby/town-square");
        await hub.Groups.Received(1).AddToGroupAsync(Connection, "tabby/cafe");
        await _heartbeat.RenewAsync();
        await _cafe.ReceivedWithAnyArgs(1).Renew(default!);
        await _room.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task GoThroughDoor_WhenTheCatIsNotOnTheDoor_IsRefused_AndStaysInItsRoom()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe")
            .ThrowsAsync(new GameRuleException("Walk up to the door to go through it."));
        RenewalsFindEveryCat();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoThroughDoor("cafe"));

        Assert.That(refused!.Message, Is.EqualTo("Walk up to the door to go through it."));
        await _cafe.DidNotReceiveWithAnyArgs().Join(default!, default!, default);
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        // Back in the room's group (it left it before asking, and joined it first on entering), so it hears the room again.
        await hub.Groups.Received(1).RemoveFromGroupAsync(Connection, "tabby/town-square");
        await hub.Groups.Received(2).AddToGroupAsync(Connection, "tabby/town-square");
        // Still in the square: its lease is renewed, and the cat can walk there.
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
        await hub.Move(1, 1);
        await _room.Received(1).Move(Tom.Id, 1, 1);
    }

    [Test]
    public async Task GoThroughDoor_LeavesTheRoomsGroupBeforeTheRoomLetsTheCatOut()
    {
        // The room tells its group the cat left: this tab, loading the next room, mustn't get that, nor anything else the
        // old room says after.
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe").Returns(new DoorTarget("cafe", "town-square"));

        await hub.GoThroughDoor("cafe");

        Received.InOrder(() =>
        {
            hub.Groups.RemoveFromGroupAsync(Connection, "tabby/town-square");
            _room.LeaveThrough(Tom.Id, Connection, "cafe");
        });
    }

    [Test]
    public async Task GoThroughDoor_WhenTheRoomCannotAnswer_StaysInItsRoom_SoALostCatIsNoticed()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe").ThrowsAsync(new TimeoutException());
        RenewalsFindEveryCat();

        Assert.ThrowsAsync<TimeoutException>(() => hub.GoThroughDoor("cafe"));

        // Renewed there: had the cat gone after all, the room says so and the tab walks back in.
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
        await hub.Groups.Received(2).AddToGroupAsync(Connection, "tabby/town-square");
    }

    [Test]
    public async Task GoThroughDoor_IntoAFullRoom_ComesBackInByTheSameDoor_AndSaysWhy()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe").Returns(new DoorTarget("cafe", "town-square"));
        _cafe.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Cat Café is full."));
        RenewalsFindEveryCat();

        var full = Assert.ThrowsAsync<HubException>(() => hub.GoThroughDoor("cafe"));

        Assert.That(full!.Message, Is.EqualTo("Cat Café is full."));
        await _room.Received(1).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, "cafe");
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
        await _cafe.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task GoThroughDoor_RefusedAfterTheTabClosed_StillTakesTheCatOut()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        var answer = new TaskCompletionSource<DoorTarget>();
        _room.LeaveThrough(Tom.Id, Connection, "cafe").Returns(answer.Task);

        // The tab closes while the room is still answering; the disconnect finds the connection between rooms.
        var going = hub.GoThroughDoor("cafe");
        await hub.OnDisconnectedAsync(null);
        answer.SetException(new GameRuleException("Walk up to the door to go through it."));
        try
        {
            await going;
        }
        catch (HubException)
        {
            // Refusing is the point; which message doesn't matter, nobody reads it.
        }

        await _room.Received(1).Leave(Tom.Id, Connection);
        await _world.Received(1).Leave(Tom.Id, Connection);
    }

    [Test]
    public async Task Reel_PaysACatchFirst_WithATokenTheTabClosingCannotCancel_ThenShowsItToTheRoom()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        // The tab is closing as it reels (a refresh).
        hub.Context.ConnectionAborted.Returns(new CancellationToken(canceled: true));
        _room.Reel(Tom.Id).Returns(new ReelView("caught", "koi", "Golden koi", 100, TotalCoins: null));
        _players.EarnCoinsAsync(Tom.Id, 100, Arg.Any<CancellationToken>()).Returns(Tom with { Coins = 140 });
        var room = Substitute.For<IGameClient>();
        _clients.Group("tabby/town-square").Returns(room);

        var reel = await hub.Reel();

        Assert.That(reel, Is.EqualTo(new ReelView("caught", "koi", "Golden koi", 100, TotalCoins: 140)));
        Received.InOrder(() =>
        {
            _players.EarnCoinsAsync(Tom.Id, 100, CancellationToken.None);
            room.CatFished(new CatchView(Tom.Id, "koi", "Golden koi", 100));
        });
    }

    [Test]
    public async Task Reel_WhenTheCatchCannotBePaid_ShowsItToNobody_AndTellsThePlayer()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.Reel(Tom.Id).Returns(new ReelView("caught", "koi", "Golden koi", 100, TotalCoins: null));
        _players.EarnCoinsAsync(Tom.Id, 100, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());
        var room = Substitute.For<IGameClient>();
        _clients.Group("tabby/town-square").Returns(room);

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Reel());

        Assert.That(refused!.Message, Is.EqualTo(GameHub.CatchNotPaidMessage));
        await room.DidNotReceiveWithAnyArgs().CatFished(default!);
    }

    [Test]
    public async Task Reel_NothingCaught_PaysAndShowsNothing()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        var missed = new ReelView("too-late", null, null, 0, TotalCoins: null);
        _room.Reel(Tom.Id).Returns(missed);
        var room = Substitute.For<IGameClient>();
        _clients.Group("tabby/town-square").Returns(room);

        Assert.That(await hub.Reel(), Is.EqualTo(missed));
        await _players.DidNotReceiveWithAnyArgs().EarnCoinsAsync(default, default, default);
        await room.DidNotReceiveWithAnyArgs().CatFished(default!);
    }

    [Test]
    public async Task EnterWorld_RecordsWhereTheCatIs_ForFriendsToFind()
    {
        var hub = Hub();

        await hub.EnterWorld("tabby");

        await _session.Received(1).Arrived(Connection, "tabby/town-square");
    }

    [Test]
    public async Task EnterWorld_WhenRecordingWhereTheCatIsFails_StillEntersTheRoom()
    {
        _session.Arrived(Connection, Arg.Any<string>()).ThrowsAsync(new TimeoutException());
        var hub = Hub();

        await hub.EnterWorld("tabby");

        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "tabby", "tabby/town-square")));
    }

    [Test]
    public async Task AddFriend_TellsTheOtherPlayersTabs_AndReturnsTheCard()
    {
        _friends.RequestAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.RequestSent));
        var hub = Hub();
        var felixTabs = Substitute.For<IGameClient>();
        _clients.User(Felix.ToString()).Returns(felixTabs);

        var card = await hub.AddFriend(Felix);

        Assert.That(card, Is.EqualTo(new PlayerCardView(Felix, "Felix", "Black", "RequestSent")));
        await felixTabs.Received(1).FriendsChanged(Tom.Id);
    }

    [Test]
    public async Task AddFriend_WhenTellingTheOtherPlayerFails_StillSucceeds()
    {
        // The request is saved by then: an unreachable backplane mustn't make it look refused (a retry would be "already asked").
        _friends.RequestAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.RequestSent));
        var hub = Hub();
        var felixTabs = Substitute.For<IGameClient>();
        felixTabs.FriendsChanged(Tom.Id).ThrowsAsync(new TimeoutException());
        _clients.User(Felix.ToString()).Returns(felixTabs);

        var card = await hub.AddFriend(Felix);

        Assert.That(card.FriendState, Is.EqualTo("RequestSent"));
    }

    [Test]
    public async Task AcceptFriend_ARuleBroken_IsThePlayersMessage_AndNobodyIsTold()
    {
        _friends.AcceptAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("There's no request to you to answer."));
        var hub = Hub();
        var felixTabs = Substitute.For<IGameClient>();
        _clients.User(Felix.ToString()).Returns(felixTabs);

        var refused = Assert.ThrowsAsync<HubException>(() => hub.AcceptFriend(Felix));

        Assert.That(refused!.Message, Is.EqualTo("There's no request to you to answer."));
        await felixTabs.DidNotReceiveWithAnyArgs().FriendsChanged(default);
    }

    [Test]
    public async Task GetFriends_ShowsWhereEachFriendIs_AsTheirSessionFindsThem()
    {
        var garfield = Guid.NewGuid();
        var oddie = Guid.NewGuid();
        var nermal = Guid.NewGuid();
        _friends.ListAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends),
            new PlayerRelation(garfield, "Garfield", FurColor.Orange, FriendState.Friends),
            new PlayerRelation(nermal, "Nermal", FurColor.Grey, FriendState.Friends),
            new PlayerRelation(oddie, "Oddie", FurColor.Cream, FriendState.RequestReceived),
        ]);
        Session(Felix).Locate().Returns(new PlayerPlace("calico", "cafe"));
        Session(garfield).Locate().Returns((PlayerPlace?)null);
        Session(nermal).Locate().ThrowsAsync(new TimeoutException());
        var oddieSession = Session(oddie);
        var hub = Hub();

        var list = await hub.GetFriends();

        Assert.That(list, Is.EqualTo(new[]
        {
            new FriendView(Felix, "Felix", "Black", "Friends", new FriendPlaceView("calico", "Calico", "cafe", "Cat Café")),
            new FriendView(garfield, "Garfield", "Orange", "Friends", null),
            new FriendView(nermal, "Nermal", "Grey", "Friends", null),
            new FriendView(oddie, "Oddie", "Cream", "RequestReceived", null),
        }));
        await oddieSession.DidNotReceive().Locate();
    }

    [Test]
    public async Task GoToFriend_InAnotherWorld_TakesAPlaceThereBeforeLeavingThisOne()
    {
        var (calico, calicoCafe) = FriendInCalicoCafe();
        var hub = Hub();
        await hub.EnterWorld("tabby");

        await hub.GoToFriend(Felix);

        Received.InOrder(() =>
        {
            calico.TryEnter(Tom.Id, Connection);
            calicoCafe.Join(Arg.Any<PlayerCard>(), Connection, null);
            _room.Leave(Tom.Id, Connection);
            _world.Leave(Tom.Id, Connection);
        });
        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "calico", "calico/cafe")));
        await _session.Received(1).Arrived(Connection, "calico/cafe");
    }

    [Test]
    public async Task GoToFriend_WhenTheirWorldIsFull_StaysWhereItIs()
    {
        var (calico, calicoCafe) = FriendInCalicoCafe();
        calico.TryEnter(Tom.Id, Connection).Returns(false);
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToFriend(Felix));

        Assert.That(refused!.Message, Does.Contain("Calico is full"));
        await calicoCafe.DidNotReceiveWithAnyArgs().Join(default!, default!, default);
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        await _world.DidNotReceiveWithAnyArgs().Leave(default, default!);
        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "tabby", "tabby/town-square")));
    }

    [Test]
    public async Task GoToFriend_WhenTheirRoomIsFull_GivesBackThePlaceInTheirWorld_AndStays()
    {
        var (calico, calicoCafe) = FriendInCalicoCafe();
        calicoCafe.Join(Arg.Any<PlayerCard>(), Connection, null).ThrowsAsync(new GameRuleException("Cat Café is full."));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToFriend(Felix));

        Assert.That(refused!.Message, Is.EqualTo("Cat Café is full."));
        await calico.Received(1).Leave(Tom.Id, Connection);
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        await _world.DidNotReceiveWithAnyArgs().Leave(default, default!);
        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "tabby", "tabby/town-square")));
    }

    [Test]
    public async Task GoToFriend_InThisWorld_GoesToTheirRoom()
    {
        _friends.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends));
        Session(Felix).Locate().Returns(new PlayerPlace("tabby", "cafe"));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        await hub.GoToFriend(Felix);

        await _cafe.Received(1).Join(Arg.Any<PlayerCard>(), Connection, null);
        await _world.DidNotReceiveWithAnyArgs().Leave(default, default!);
        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "tabby", "tabby/cafe")));
    }

    [Test]
    public async Task GoToFriend_WhoIsNotAFriend_IsRefused()
    {
        _friends.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("You can only go to your friends."));
        var felixSession = Session(Felix);
        felixSession.Locate().Returns(new PlayerPlace("tabby", "cafe"));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToFriend(Felix));

        Assert.That(refused!.Message, Is.EqualTo("You can only go to your friends."));
        await felixSession.DidNotReceive().Locate();
        await _cafe.DidNotReceiveWithAnyArgs().Join(default!, default!, default);
    }

    [Test]
    public async Task GoToFriend_WhoIsOffline_SaysSo()
    {
        _friends.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends));
        Session(Felix).Locate().Returns((PlayerPlace?)null);
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToFriend(Felix));

        Assert.That(refused!.Message, Is.EqualTo("Felix isn't in a room right now."));
    }

    // Felix is Tom's friend, in Calico's café; returns Calico's world and café grains.
    private (IWorldGrain World, IRoomGrain Cafe) FriendInCalicoCafe()
    {
        _friends.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends));
        Session(Felix).Locate().Returns(new PlayerPlace("calico", "cafe"));
        var calico = Substitute.For<IWorldGrain>();
        var calicoCafe = Substitute.For<IRoomGrain>();
        _grains.GetGrain<IWorldGrain>("calico", null).Returns(calico);
        _grains.GetGrain<IRoomGrain>("calico/cafe", null).Returns(calicoCafe);
        calico.TryEnter(Tom.Id, Connection).Returns(true);
        return (calico, calicoCafe);
    }

    private IPlayerSessionGrain Session(Guid playerId)
    {
        var session = Substitute.For<IPlayerSessionGrain>();
        _grains.GetGrain<IPlayerSessionGrain>(playerId, null).Returns(session);
        return session;
    }

    // The world and rooms answer every renewal with nobody lost.
    private void RenewalsFindEveryCat()
    {
        _world.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
        _room.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
        _cafe.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
    }

    [Test]
    public async Task EnterWorld_TheRoomIsToldWhatTheCatWears()
    {
        _players.FindAsync(Tom.Id, Arg.Any<CancellationToken>())
            .Returns(Tom with { FurColor = FurColor.Tuxedo, Hat = "crown", Collar = "bell-collar" });
        var hub = Hub();

        await hub.EnterWorld("tabby");

        await _room.Received(1).Join(
            new PlayerCard(Tom.Id, "Tom", "Tuxedo", "crown", "bell-collar"), Connection, null);
    }

    [Test]
    public async Task Wear_SavesIt_ThenShowsTheRoomTheCatIsIn()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _shop.WearAsync(Tom.Id, "bow", Arg.Any<CancellationToken>()).Returns(Dressed("bow", null));

        var shop = await hub.Wear("bow");

        await _room.Received(1).Dress(new OutfitView(Tom.Id, "Grey", "bow", null));
        Assert.Multiple(() =>
        {
            Assert.That(shop.Hat, Is.EqualTo("bow"));
            Assert.That(shop.Coins, Is.EqualTo(15));
            Assert.That(shop.Owned, Is.EqualTo(new[] { "fur-grey", "bow" }));
            Assert.That(shop.Items.Select(i => i.Id), Is.EqualTo(ShopCatalog.All.Select(i => i.Id)), "the whole catalogue, in order");
            Assert.That(shop.Items.Single(i => i.Id == "fur-calico"), Is.EqualTo(
                new ShopItemView("fur-calico", "Calico fur", "Fur", ShopCatalog.PatternedFurPrice, "Calico")));
        });
    }

    [Test]
    public async Task TakeOff_ShowsTheRoomWhereThePlayersCurrentTabHasTheCat()
    {
        // This tab is in no room (the world list, say); the player's cat is in the café, played from another tab.
        _session.Locate().Returns(new PlayerPlace("tabby", "cafe"));
        _shop.TakeOffAsync(Tom.Id, "bow", Arg.Any<CancellationToken>()).Returns(Dressed(null, "red-collar"));
        var hub = Hub();

        await hub.TakeOff("bow");

        await _cafe.Received(1).Dress(new OutfitView(Tom.Id, "Grey", null, "red-collar"));
        await _room.DidNotReceiveWithAnyArgs().Dress(default!);
    }

    [Test]
    public async Task Wear_ARuleBroken_IsThePlayersMessage_AndTheRoomIsntTold()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _shop.WearAsync(Tom.Id, "crown", Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("Buy the Crown first."));

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Wear("crown"));

        Assert.That(refused!.Message, Is.EqualTo("Buy the Crown first."));
        await _room.DidNotReceiveWithAnyArgs().Dress(default!);
    }

    [Test]
    public async Task Wear_WhenTheRoomCantBeTold_StillSucceeds()
    {
        // Saved by then: the room shows it to whoever arrives next, and the player's next arrival.
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _shop.WearAsync(Tom.Id, "bow", Arg.Any<CancellationToken>()).Returns(Dressed("bow", null));
        _room.Dress(Arg.Any<OutfitView>()).ThrowsAsync(new TimeoutException());

        var shop = await hub.Wear("bow");

        Assert.That(shop.Hat, Is.EqualTo("bow"));
    }

    [Test]
    public async Task Buy_PutsNothingOn_SoTheRoomIsntTold()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _shop.BuyAsync(Tom.Id, "bow", Arg.Any<CancellationToken>()).Returns(Dressed(null, null));

        var shop = await hub.Buy("bow");

        Assert.That(shop.Owned, Does.Contain("bow"));
        await _room.DidNotReceiveWithAnyArgs().Dress(default!);
    }

    [Test]
    public void Buy_WithoutTheCoins_IsThePlayersMessage()
    {
        _shop.BuyAsync(Tom.Id, "crown", Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("The Crown costs 1000 coins. You have 15."));
        var hub = Hub();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Buy("crown"));

        Assert.That(refused!.Message, Is.EqualTo("The Crown costs 1000 coins. You have 15."));
    }

    [Test]
    public async Task GetPlayerCard_SaysWhatTheCatWears_ByName()
    {
        _friends.GetAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.None, "top-hat", "bell-collar"));
        var hub = Hub();

        var card = await hub.GetPlayerCard(Felix);

        Assert.That(card, Is.EqualTo(new PlayerCardView(Felix, "Felix", "Black", "None", "Top hat", "Bell collar")));
    }

    private static Wardrobe Dressed(string? hat, string? collar) =>
        new(Tom.Id, 15, ["fur-grey", "bow"], FurColor.Grey, hat, collar);

    private GameHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(Connection);
        context.User.Returns(PlayerClaims.For(Tom));
        context.Items.Returns(new ConnectionItems());
        context.ConnectionAborted.Returns(CancellationToken.None);
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());
        return new GameHub(_grains, _players, _friends, _shop, _homes, _heartbeat, metrics, NullLogger<GameHub>.Instance)
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = _clients = Substitute.For<IHubCallerClients<IGameClient>>(),
        };
    }
}
