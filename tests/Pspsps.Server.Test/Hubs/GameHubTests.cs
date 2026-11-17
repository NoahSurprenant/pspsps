using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pspsps.Application.Friends;
using Pspsps.Application.Players;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;
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

        await _cafe.DidNotReceiveWithAnyArgs().Join(default!, default!);
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
            calicoCafe.Join(Arg.Any<PlayerCard>(), Connection);
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
        await calicoCafe.DidNotReceiveWithAnyArgs().Join(default!, default!);
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        await _world.DidNotReceiveWithAnyArgs().Leave(default, default!);
        Assert.That(_heartbeat.PlaceOf(Connection), Is.EqualTo(new ConnectionPlace(Tom.Id, "tabby", "tabby/town-square")));
    }

    [Test]
    public async Task GoToFriend_WhenTheirRoomIsFull_GivesBackThePlaceInTheirWorld_AndStays()
    {
        var (calico, calicoCafe) = FriendInCalicoCafe();
        calicoCafe.Join(Arg.Any<PlayerCard>(), Connection).ThrowsAsync(new GameRuleException("Cat Café is full."));
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

        await _cafe.Received(1).Join(Arg.Any<PlayerCard>(), Connection);
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
        await _cafe.DidNotReceiveWithAnyArgs().Join(default!, default!);
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

    private GameHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(Connection);
        context.User.Returns(PlayerClaims.For(Tom));
        context.Items.Returns(new ConnectionItems());
        context.ConnectionAborted.Returns(CancellationToken.None);
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());
        return new GameHub(_grains, _players, _friends, _heartbeat, metrics, NullLogger<GameHub>.Instance)
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = _clients = Substitute.For<IHubCallerClients<IGameClient>>(),
        };
    }
}
