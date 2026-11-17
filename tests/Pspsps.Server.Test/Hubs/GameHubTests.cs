using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pspsps.Application.Players;
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

    private ServiceProvider _services = null!;
    private IGrainFactory _grains = null!;
    private IWorldGrain _world = null!;
    private IRoomGrain _room = null!;
    private IPlayerService _players = null!;
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
        await hub.Groups.DidNotReceiveWithAnyArgs().RemoveFromGroupAsync(default!, default!);
        await _room.DidNotReceiveWithAnyArgs().Leave(default, default!);
        // Still in the square: its lease is renewed, and the cat can walk there.
        await _heartbeat.RenewAsync();
        await _room.ReceivedWithAnyArgs(1).Renew(default!);
        await hub.Move(1, 1);
        await _room.Received(1).Move(Tom.Id, 1, 1);
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

    // The world and rooms answer every renewal with nobody lost.
    private void RenewalsFindEveryCat()
    {
        _world.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
        _room.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
        _cafe.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>([]));
    }

    private GameHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(Connection);
        context.User.Returns(PlayerClaims.For(Tom));
        context.Items.Returns(new ConnectionItems());
        context.ConnectionAborted.Returns(CancellationToken.None);
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());
        return new GameHub(_grains, _players, _heartbeat, metrics, NullLogger<GameHub>.Instance)
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = _clients = Substitute.For<IHubCallerClients<IGameClient>>(),
        };
    }
}
