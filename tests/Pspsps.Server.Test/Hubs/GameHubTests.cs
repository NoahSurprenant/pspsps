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
            _grains, Substitute.For<IHubContext<GameHub, IGameClient>>(), TimeProvider.System, NullLogger<SessionHeartbeat>.Instance);
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

    private GameHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(Connection);
        context.User.Returns(PlayerClaims.For(Tom));
        context.Items.Returns(new ConnectionItems());
        context.ConnectionAborted.Returns(CancellationToken.None);
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());
        return new GameHub(_grains, _players, _heartbeat, metrics)
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = _clients = Substitute.For<IHubCallerClients<IGameClient>>(),
        };
    }
}
