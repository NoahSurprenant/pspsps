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
using Pspsps.Application.Safety;
using Pspsps.Application.Shop;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;
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
    private IMuteService _mutes = null!;
    private IReportService _reports = null!;
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
        _mutes = Substitute.For<IMuteService>();
        _reports = Substitute.For<IReportService>();
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
            Assert.That(shop.MaxOfEachFurniture, Is.EqualTo(ShopCatalog.MaxOfEachFurniture), "the client keeps the server's limit");
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
        _shop.BuyAsync(Tom.Id, Arg.Any<string?>(), "bow", Arg.Any<CancellationToken>()).Returns(Dressed(null, null));

        var shop = await hub.Buy("bow");

        Assert.That(shop.Owned, Does.Contain("bow"));
        // From the catalogue of the room the tab's cat is in, which the shop's rule (the Domain's) decides on.
        await _shop.Received().BuyAsync(Tom.Id, "town-square", "bow", Arg.Any<CancellationToken>());
        await _room.DidNotReceiveWithAnyArgs().Dress(default!);
    }

    [Test]
    public async Task Buy_FromATabThatWasReplaced_IsRefused_AndNothingIsBought()
    {
        // This tab was in the shop; another tab took the cat over (and may have walked it anywhere): this one buys nothing.
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _session.IsCurrent(Connection).Returns(false);

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Buy("bow"));

        Assert.That(refused!.Message, Is.EqualTo(GameHub.ReplacedMessage));
        await _shop.DidNotReceiveWithAnyArgs().BuyAsync(default, default, default!, default);
    }

    [Test]
    public void Buy_WithoutTheCoins_IsThePlayersMessage()
    {
        _shop.BuyAsync(Tom.Id, null, "crown", Arg.Any<CancellationToken>())
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

        Assert.That(card, Is.EqualTo(new PlayerCardView(Felix, "Felix", "Black", "None", "Top hat", "Bell collar", Hat: "top-hat", Collar: "bell-collar")));
    }

    [Test]
    public async Task VisitHome_JoinsTheHome_StayingInItsWorld_AndRenewsBoth()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        RenewalsFindEveryCat();
        felixHome.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));

        var there = await hub.VisitHome(Felix);

        Assert.That(there.WorldId, Is.EqualTo("tabby"), "a home is in no world: the cat is still in the one it came from");
        await _room.Received(1).Leave(Tom.Id, Connection);
        await felixHome.Received(1).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
        await hub.Groups.Received(1).AddToGroupAsync(Connection, RoomKey.ForHome(Felix));
        await _session.Received(1).Arrived(Connection, RoomKey.ForHome(Felix));
        await _heartbeat.RenewAsync();
        await _world.ReceivedWithAnyArgs(1).Renew(default!);
        await felixHome.ReceivedWithAnyArgs(1).Renew(default!);
        await _room.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task VisitHome_ThatTheHomeRefuses_LeavesTheCatWhereItIs()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("Only friends can visit that home."));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.VisitHome(Felix));

        Assert.That(refused!.Message, Is.EqualTo("Only friends can visit that home."));
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        await felixHome.DidNotReceiveWithAnyArgs().Join(default!, default!, default);
    }

    [Test]
    public void VisitHome_BeforePickingAWorld_IsRefused()
    {
        var refused = Assert.ThrowsAsync<HubException>(() => Hub().GoHome());

        Assert.That(refused!.Message, Is.EqualTo("Pick a world first."));
    }

    [Test]
    public async Task GoThroughDoor_OutOfAHome_GoesToTheTownSquareOfItsWorld_AtTheSpawnPoint()
    {
        var home = HomeOf(Tom.Id);
        _homes.VisitAsync(Tom.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns("Tom");
        home.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Tom.Id));
        home.LeaveThrough(Tom.Id, Connection, "out").Returns(new DoorTarget("town-square", null));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        await hub.GoHome();
        _room.ClearReceivedCalls();

        await hub.GoThroughDoor("out");

        await _room.Received(1).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
        await hub.Groups.Received(1).RemoveFromGroupAsync(Connection, RoomKey.ForHome(Tom.Id));
    }

    [Test]
    public async Task LetIntoHome_SendsOutWhoTheHomeSaysMustGo_AndShowsTheChange()
    {
        var home = HomeOf(Tom.Id);
        _homes.LetInAsync(Tom.Id, HomeVisitors.Friends, Arg.Any<CancellationToken>()).Returns(Plan(Tom.Id, "Tom", HomeVisitors.Friends));
        home.CatsHere().Returns([Tom.Id, Felix]);
        _homes.MustLeaveAsync(Tom.Id, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 2), Arg.Any<CancellationToken>())
            .Returns([Felix]);
        var hub = Hub();

        var view = await hub.LetIntoHome("Friends");

        Assert.That(view.Visitors, Is.EqualTo("Friends"));
        await home.Received(1).Furnish(Arg.Is<HomeView>(h => h.OwnerId == Tom.Id && h.Visitors == "Friends"));
        await home.Received(1).SendOut(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Felix })),
            "Tom changed who can visit their home, so you went back to Town Square.");
    }

    [Test]
    public async Task LetIntoHome_WithNobodyToSendOut_SendsNoOneOut()
    {
        var home = HomeOf(Tom.Id);
        _homes.LetInAsync(Tom.Id, HomeVisitors.Anyone, Arg.Any<CancellationToken>()).Returns(Plan(Tom.Id, "Tom", HomeVisitors.Anyone));
        home.CatsHere().Returns([Felix]);
        _homes.MustLeaveAsync(Tom.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        await Hub().LetIntoHome("Anyone");

        await home.DidNotReceiveWithAnyArgs().SendOut(default!, default!);
    }

    [TestCase("Sometimes")]
    [TestCase("7")]
    [TestCase("")]
    public async Task LetIntoHome_SomethingThatIsntAChoice_IsRefused(string visitors)
    {
        var refused = Assert.ThrowsAsync<HubException>(() => Hub().LetIntoHome(visitors));

        Assert.That(refused!.Message, Is.EqualTo("That isn't a choice."));
        await _homes.DidNotReceiveWithAnyArgs().LetInAsync(default, default, default);
    }

    [Test]
    public async Task PlaceFurniture_ThatCantBeShown_IsStillSaved()
    {
        var home = HomeOf(Tom.Id);
        _homes.PlaceAsync(Tom.Id, "sofa", 1, 2, 3, Arg.Any<CancellationToken>()).Returns(Plan(Tom.Id, "Tom"));
        home.Furnish(Arg.Any<HomeView>()).ThrowsAsync(new TimeoutException());

        var view = await Hub().PlaceFurniture("sofa", 1, 2, 3);

        Assert.That(view.OwnerId, Is.EqualTo(Tom.Id));
        await _homes.Received(1).PlaceAsync(Tom.Id, "sofa", 1, 2, 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public void PlaceFurniture_ThatTheHomeRefuses_IsThePlayersMessage()
    {
        _homes.PlaceAsync(Tom.Id, "sofa", null, null, 0, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("Buy the Sofa first."));

        var refused = Assert.ThrowsAsync<HubException>(() => Hub().PlaceFurniture("sofa", null, null, 0));

        Assert.That(refused!.Message, Is.EqualTo("Buy the Sofa first."));
    }

    [Test]
    public async Task GetFriends_AFriendAtHome_IsInTheirHome_WhichIsInNoWorld()
    {
        _friends.ListAsync(Tom.Id, Arg.Any<CancellationToken>())
            .Returns([new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends)]);
        _homes.OpenToAsync(Tom.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { Felix });
        Session(Felix).Locate().Returns(new PlayerPlace(RoomKey.Homes, Felix.ToString()));

        var friend = (await Hub().GetFriends()).Single();

        Assert.That(friend.Place, Is.EqualTo(new FriendPlaceView("", "", "home", "Felix's home", Felix)));
        Assert.That(friend.CanVisitHome, Is.True);
    }

    [Test]
    public async Task RemoveFriend_SendsEachOutOfTheOthersHome_WhenItsHomeNoLongerLetsThemStay()
    {
        var tomsHome = HomeOf(Tom.Id);
        var felixsHome = HomeOf(Felix);
        _friends.UnfriendAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.None));
        tomsHome.CatsHere().Returns([Felix]);
        // Each, as far as their sessions say, in the other's home (the room has the last word).
        Session(Felix).Locate().Returns(new PlayerPlace(RoomKey.Homes, Tom.Id.ToString()));
        _session.Locate().Returns(new PlayerPlace(RoomKey.Homes, Felix.ToString()));
        felixsHome.CatsHere().Returns([Tom.Id, Felix]);
        _homes.MustLeaveAsync(Tom.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([Felix]);
        _homes.MustLeaveAsync(Felix, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([Tom.Id]);
        _players.FindAsync(Felix, Arg.Any<CancellationToken>()).Returns(new PlayerProfile(Felix, "Felix", FurColor.Black, 0, IsGuest: true));

        await Hub().RemoveFriend(Felix);

        await tomsHome.Received(1).SendOut(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Felix })), Arg.Any<string>());
        await felixsHome.Received(1).SendOut(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Tom.Id })), Arg.Any<string>());
    }

    [Test]
    public async Task RemoveFriend_AsksTheHome_WhenTheSessionDoesntSayWhereTheCatIs()
    {
        // Felix's session doesn't know where he is (its Arrived failed, say), but Tom's home has his cat: the home is asked.
        var tomsHome = HomeOf(Tom.Id);
        HomeOf(Felix);
        _friends.UnfriendAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.None));
        Session(Felix).Locate().Returns((PlayerPlace?)null);
        tomsHome.CatsHere().Returns([Felix]);
        _homes.MustLeaveAsync(Tom.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([Felix]);

        await Hub().RemoveFriend(Felix);

        await tomsHome.Received(1).SendOut(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Felix })), Arg.Any<string>());
    }

    [Test]
    public async Task RemoveFriend_WithTheOtherSomewhereElse_DoesntOpenTheirHome()
    {
        // Felix is in the café, as his session says: Tom's home isn't asked (nor opened, if nobody is in it).
        var tomsHome = HomeOf(Tom.Id);
        HomeOf(Felix);
        _friends.UnfriendAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .Returns(new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.None));
        Session(Felix).Locate().Returns(new PlayerPlace("tabby", "cafe"));

        await Hub().RemoveFriend(Felix);

        await tomsHome.DidNotReceive().CatsHere();
    }

    [Test]
    public async Task DeclineOrCancel_ARequest_AsksNoHome()
    {
        // Only friends ending a friendship can lose a way in: answering a request changes no home.
        var tomsHome = HomeOf(Tom.Id);
        var felixsHome = HomeOf(Felix);
        var none = new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.None);
        _friends.DeclineAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns(none);
        _friends.CancelAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns(none);
        Session(Felix).Locate().Returns(new PlayerPlace(RoomKey.Homes, Tom.Id.ToString()));
        tomsHome.CatsHere().Returns([Felix]);
        felixsHome.CatsHere().Returns([Tom.Id]);

        var hub = Hub();
        await hub.DeclineFriend(Felix);
        await hub.CancelFriendRequest(Felix);

        await tomsHome.DidNotReceive().CatsHere();
        await felixsHome.DidNotReceive().CatsHere();
        await _homes.DidNotReceiveWithAnyArgs().MustLeaveAsync(default, default!, default);
    }

    [Test]
    public async Task LetIntoHome_WhenSendingOutFails_StillAnswersWithTheSavedSetting()
    {
        var home = HomeOf(Tom.Id);
        _homes.LetInAsync(Tom.Id, HomeVisitors.Nobody, Arg.Any<CancellationToken>()).Returns(Plan(Tom.Id, "Tom", HomeVisitors.Nobody));
        home.CatsHere().ThrowsAsync(new TimeoutException());

        var view = await Hub().LetIntoHome("Nobody");

        Assert.That(view.Visitors, Is.EqualTo("Nobody"));
    }

    [Test]
    public async Task VisitHome_ThatCantBeOpened_ComesBackIntoTheRoomItLeft()
    {
        // Not a rule broken: the home's grain couldn't read it (the database timed out).
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new TimeoutException());
        var hub = Hub();
        await hub.EnterWorld("tabby");
        RenewalsFindEveryCat();

        Assert.ThrowsAsync<TimeoutException>(() => hub.VisitHome(Felix));

        await _room.Received(2).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task GoThroughDoor_OutOfAnotherPlayersHome_IntoAFullTownSquare_DoesntGoStraightBackIn()
    {
        // The home may no longer let the cat in: it goes back through the checked way, after the renewal says it's lost.
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        felixHome.LeaveThrough(Tom.Id, Connection, "out").Returns(new DoorTarget("town-square", null));
        felixHome.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([Connection]));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        await hub.VisitHome(Felix);
        _room.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Town Square is full."));
        RenewalsFindEveryCat();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoThroughDoor("out"));

        Assert.That(refused!.Message, Is.EqualTo("Town Square is full."));
        await felixHome.Received(1).Join(Arg.Any<PlayerCard>(), Connection, Arg.Any<string?>());
        await _heartbeat.RenewAsync();
        await felixHome.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task GoThroughDoor_WhenTheRoomPastItFails_ComesBackInByTheDoor()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        _room.LeaveThrough(Tom.Id, Connection, "cafe").Returns(new DoorTarget("cafe", "town-square"));
        _cafe.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new TimeoutException());
        RenewalsFindEveryCat();

        Assert.ThrowsAsync<TimeoutException>(() => hub.GoThroughDoor("cafe"));

        await _room.Received(1).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, "cafe");
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task GoToRoom_IntoAFullRoom_FromAHomeThatSentTheCatOut_DoesntGoBackIntoTheHome()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        await hub.VisitHome(Felix);
        // Felix closed his home and sent Tom out; Town Square is full.
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException("That home isn't open to visitors right now."));
        _room.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Town Square is full."));

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToRoom("town-square"));

        Assert.That(refused!.Message, Is.EqualTo("Town Square is full."));
        await felixHome.Received(1).Join(Arg.Any<PlayerCard>(), Connection, null);
    }

    [Test]
    public async Task GoToRoom_FullWhenTheRoomLeftCantAnswerEither_LeavesTheRenewalToWalkTheTabBackIn()
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");
        await hub.GoToRoom("cafe");
        _room.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Town Square is full."));
        _cafe.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new TimeoutException());
        RenewalsFindEveryCat();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.GoToRoom("town-square"));

        Assert.That(refused!.Message, Is.EqualTo("Town Square is full."), "the reason, not the failure after it");
        await _heartbeat.RenewAsync();
        await _cafe.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task GoToRoom_FullFromAnotherPlayersHome_DoesntGoBackInto_ButLetsTheRenewalSendTheTabBack()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        felixHome.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        await hub.VisitHome(Felix);
        _room.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Town Square is full."));
        RenewalsFindEveryCat();

        Assert.ThrowsAsync<HubException>(() => hub.GoToRoom("town-square"));

        await felixHome.Received(1).Join(Arg.Any<PlayerCard>(), Connection, null);
        await _heartbeat.RenewAsync();
        await felixHome.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task VisitHome_WhenTheSecondCheckFails_TakesTheCatBackOut()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult("Felix"), Task.FromException<string>(new TimeoutException()));
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        Assert.ThrowsAsync<TimeoutException>(() => hub.VisitHome(Felix));

        await felixHome.Received(1).Leave(Tom.Id, Connection);
        await _room.Received(2).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
    }

    [Test]
    public async Task GetFriends_TwoFriendsInTheSameHome_ReadItsOwnerOnce()
    {
        var garfield = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        _friends.ListAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new PlayerRelation(Felix, "Felix", FurColor.Black, FriendState.Friends),
            new PlayerRelation(garfield, "Garfield", FurColor.Orange, FriendState.Friends),
        ]);
        Session(Felix).Locate().Returns(new PlayerPlace(RoomKey.Homes, stranger.ToString()));
        Session(garfield).Locate().Returns(new PlayerPlace(RoomKey.Homes, stranger.ToString()));
        _players.FindAsync(stranger, Arg.Any<CancellationToken>()).Returns((PlayerProfile?)null);

        var friends = await Hub().GetFriends();

        Assert.That(friends.Select(f => f.Place), Is.All.Null, "a home whose owner is gone shows nowhere");
        await _players.Received(1).FindAsync(stranger, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VisitHome_ThatIsFull_ComesBackIntoTheRoomItLeft_AndSaysWhy()
    {
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns("Felix");
        felixHome.Join(default!, default!, default).ThrowsAsyncForAnyArgs(new GameRuleException("Felix's home is full."));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        RenewalsFindEveryCat();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.VisitHome(Felix));

        Assert.That(refused!.Message, Is.EqualTo("Felix's home is full."));
        await _room.Received(2).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
    }

    [Test]
    public async Task VisitHome_ClosedToThePlayerWhileTheyCameIn_TakesThemBackOut()
    {
        // The owner made the home stricter between the check and the join: the owner's look at who is in it may have
        // missed this cat, so the cat checks again once in, and goes back out.
        var felixHome = HomeOf(Felix);
        _homes.VisitAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult("Felix"),
            Task.FromException<string>(new DomainException("That home isn't open to visitors right now.")));
        felixHome.Join(default!, default!, default).ReturnsForAnyArgs(HomeSnapshot(Felix));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        RenewalsFindEveryCat();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.VisitHome(Felix));

        Assert.That(refused!.Message, Is.EqualTo("That home isn't open to visitors right now."));
        await felixHome.Received(1).Leave(Tom.Id, Connection);
        await _room.Received(2).Join(Arg.Is<PlayerCard>(c => c.PlayerId == Tom.Id), Connection, null);
        await _heartbeat.RenewAsync();
        await felixHome.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    // The grain of <owner>'s home.
    private IRoomGrain HomeOf(Guid owner)
    {
        var home = Substitute.For<IRoomGrain>();
        _grains.GetGrain<IRoomGrain>(RoomKey.ForHome(owner), null).Returns(home);
        return home;
    }

    private static HomePlan Plan(Guid owner, string name, HomeVisitors visitors = HomeVisitors.Friends) =>
        new(owner, name, visitors, []);

    private static RoomSnapshot HomeSnapshot(Guid owner) =>
        new(RoomKey.Homes, "home", "A home", 12, 9, [], [], [], null, [], [], new HomeView(owner, "Owner", "Friends", []));

    private static Wardrobe Dressed(string? hat, string? collar) =>
        new(Tom.Id, 15, new Dictionary<string, int> { ["fur-grey"] = 1, ["bow"] = 1 }, FurColor.Grey, hat, collar);

    [Test]
    public async Task GetMutes_ListsWhomThePlayerMuted()
    {
        _mutes.ListAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns([new MutedPlayer(Felix, "Felix", FurColor.Black)]);
        var hub = Hub();

        var mutes = await hub.GetMutes();

        Assert.That(mutes, Is.EqualTo(new[] { new MutedView(Felix, "Felix", "Black") }));
    }

    [Test]
    public async Task Mute_TellsEveryTabOfThePlayer_AndReturnsTheList()
    {
        _mutes.MuteAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns([new MutedPlayer(Felix, "Felix", FurColor.Black)]);
        var hub = Hub();
        var tomsTabs = Substitute.For<IGameClient>();
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        var mutes = await hub.Mute(Felix);

        Assert.That(mutes, Is.EqualTo(new[] { new MutedView(Felix, "Felix", "Black") }));
        await tomsTabs.Received(1).MutesChanged(Arg.Is<IReadOnlyList<MutedView>>(list => list.SequenceEqual(mutes)));
    }

    [Test]
    public async Task Unmute_TellsEveryTabOfThePlayer()
    {
        _mutes.UnmuteAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns([]);
        var hub = Hub();
        var tomsTabs = Substitute.For<IGameClient>();
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        var mutes = await hub.Unmute(Felix);

        Assert.That(mutes, Is.Empty);
        await tomsTabs.Received(1).MutesChanged(Arg.Is<IReadOnlyList<MutedView>>(list => list.Count == 0));
    }

    [Test]
    public async Task Mute_ARuleBroken_IsThePlayersMessage_AndNoTabIsTold()
    {
        _mutes.MuteAsync(Tom.Id, Tom.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new DomainException("You can't mute yourself."));
        var hub = Hub();
        var tomsTabs = Substitute.For<IGameClient>();
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Mute(Tom.Id));

        Assert.That(refused!.Message, Is.EqualTo("You can't mute yourself."));
        await tomsTabs.DidNotReceiveWithAnyArgs().MutesChanged(default!);
    }

    [Test]
    public async Task Mute_WhenTellingTheTabsFails_StillSucceeds()
    {
        _mutes.MuteAsync(Tom.Id, Felix, Arg.Any<CancellationToken>()).Returns([new MutedPlayer(Felix, "Felix", FurColor.Black)]);
        var hub = Hub();
        var tomsTabs = Substitute.For<IGameClient>();
        tomsTabs.MutesChanged(Arg.Any<IReadOnlyList<MutedView>>()).ThrowsAsync(new TimeoutException());
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        Assert.That(await hub.Mute(Felix), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Report_FilesTheRoomsOwnChat_FromWhereTheTabIs()
    {
        var said = new ChatView(Felix, "meanie", DateTimeOffset.UnixEpoch);
        _room.RecentChat().Returns([said]);
        _reports.FileAsync(
                Tom.Id, Felix, ReportReason.MeanChat, "rude", "tabby", "tabby/town-square",
                Arg.Any<IReadOnlyList<ChatLine>>(), false, Arg.Any<CancellationToken>())
            .Returns(new ReportOutcome(false, []));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        var tomsTabs = Substitute.For<IGameClient>();
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        var receipt = await hub.Report(Felix, "MeanChat", "rude", alsoMute: false);

        Assert.That(receipt.Muted, Is.False);
        await _reports.Received(1).FileAsync(
            Tom.Id, Felix, ReportReason.MeanChat, "rude", "tabby", "tabby/town-square",
            Arg.Is<IReadOnlyList<ChatLine>>(chat => chat.SequenceEqual(new[] { new ChatLine(Felix, "meanie", DateTimeOffset.UnixEpoch) })),
            false, Arg.Any<CancellationToken>());
        await tomsTabs.DidNotReceiveWithAnyArgs().MutesChanged(default!);
    }

    [Test]
    public async Task Report_AlsoMuting_TellsEveryTabOfThePlayer()
    {
        _room.RecentChat().Returns([]);
        MutedPlayer[] after = [new(Felix, "Felix", FurColor.Black)];
        _reports.FileAsync(
                Tom.Id, Felix, ReportReason.Spam, null, "tabby", "tabby/town-square",
                Arg.Any<IReadOnlyList<ChatLine>>(), true, Arg.Any<CancellationToken>())
            .Returns(new ReportOutcome(true, after));
        var hub = Hub();
        await hub.EnterWorld("tabby");
        var tomsTabs = Substitute.For<IGameClient>();
        _clients.User(Tom.Id.ToString()).Returns(tomsTabs);

        var receipt = await hub.Report(Felix, "Spam", null, alsoMute: true);

        Assert.That(receipt.Muted, Is.True);
        Assert.That(receipt.Mutes, Is.EqualTo(new[] { new MutedView(Felix, "Felix", "Black") }));
        await tomsTabs.Received(1).MutesChanged(Arg.Is<IReadOnlyList<MutedView>>(list => list.Count == 1));
    }

    [Test]
    public async Task Report_WhenTheRoomsChatCantBeRead_IsFiledWithout()
    {
        _room.RecentChat().ThrowsAsync(new TimeoutException());
        _reports.FileAsync(default, default, default, default, default!, default!, default!, default, default)
            .ReturnsForAnyArgs(new ReportOutcome(false, []));
        var hub = Hub();
        await hub.EnterWorld("tabby");

        await hub.Report(Felix, "Spam", null, alsoMute: false);

        await _reports.Received(1).FileAsync(
            Tom.Id, Felix, ReportReason.Spam, null, "tabby", "tabby/town-square",
            Arg.Is<IReadOnlyList<ChatLine>>(chat => chat.Count == 0), false, Arg.Any<CancellationToken>());
    }

    [TestCase("Rude")]
    [TestCase("1")]
    [TestCase("MeanChat, Spam")]
    [TestCase("meanchat")]
    public async Task Report_AReasonNotOnTheList_IsRefused(string reason)
    {
        var hub = Hub();
        await hub.EnterWorld("tabby");

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Report(Felix, reason, null, alsoMute: false));

        Assert.That(refused!.Message, Is.EqualTo("Pick a reason."));
        await _reports.DidNotReceiveWithAnyArgs().FileAsync(default, default, default, default, default!, default!, default!, default, default);
    }

    [Test]
    public async Task Report_FromNoRoom_IsRefused()
    {
        var hub = Hub();

        var refused = Assert.ThrowsAsync<HubException>(() => hub.Report(Felix, "Spam", null, alsoMute: false));

        Assert.That(refused!.Message, Is.EqualTo("You can only report a cat from a room."));
        await _reports.DidNotReceiveWithAnyArgs().FileAsync(default, default, default, default, default!, default!, default!, default, default);
    }

    [Test]
    public void Report_ARuleBroken_IsThePlayersMessage()
    {
        _room.RecentChat().Returns([]);
        _reports.FileAsync(default, default, default, default, default!, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new DomainException("You can't report yourself."));
        var hub = Hub();

        var refused = Assert.ThrowsAsync<HubException>(async () =>
        {
            await hub.EnterWorld("tabby");
            await hub.Report(Tom.Id, "Spam", null, alsoMute: false);
        });

        Assert.That(refused!.Message, Is.EqualTo("You can't report yourself."));
    }

    private GameHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(Connection);
        context.User.Returns(PlayerClaims.For(Tom));
        context.Items.Returns(new ConnectionItems());
        context.ConnectionAborted.Returns(CancellationToken.None);
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());
        return new GameHub(_grains, _players, _friends, _shop, _homes, _mutes, _reports, _heartbeat, metrics, NullLogger<GameHub>.Instance)
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = _clients = Substitute.For<IHubCallerClients<IGameClient>>(),
        };
    }
}
