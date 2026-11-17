using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Test.Hubs;

public sealed class SessionHeartbeatTests
{
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly Guid Garfield = Guid.NewGuid();

    // NUnit runs every test on one instance of this class: fresh substitutes each time.
    private IGrainFactory _grains = null!;
    private IHubContext<GameHub, IGameClient> _hub = null!;
    private SessionHeartbeat _heartbeat = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void Substitutes()
    {
        _grains = Substitute.For<IGrainFactory>();
        _hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _heartbeat = new SessionHeartbeat(
            _grains,
            _hub,
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
    public async Task RenewAsync_RenewsEachWorldAndRoomOnce_WithTheConnectionsInIt()
    {
        var tabby = World("tabby");
        var square = Room("tabby/town-square");
        var cafe = Room("tabby/cafe");

        _heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/town-square"));
        _heartbeat.Track("felix-tab", new(Felix, "tabby", "tabby/town-square"));
        // In the world, on the way between rooms.
        _heartbeat.Track("garfield-tab", new(Garfield, "tabby", null));
        // Was in the café, then left the world.
        _heartbeat.Track("gone-tab", new(Guid.NewGuid(), "tabby", "tabby/cafe"));
        _heartbeat.Track("gone-tab", null);

        await _heartbeat.RenewAsync();

        await tabby.Received(1).Renew(Arg.Is<IReadOnlyList<PlayerSession>>(sessions => Same(sessions,
            new PlayerSession(Tom, "tom-tab"), new PlayerSession(Felix, "felix-tab"), new PlayerSession(Garfield, "garfield-tab"))));
        await square.Received(1).Renew(Arg.Is<IReadOnlyList<PlayerSession>>(sessions => Same(sessions,
            new PlayerSession(Tom, "tom-tab"), new PlayerSession(Felix, "felix-tab"))));
        await cafe.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task RenewAsync_WithNobodyHere_CallsNoGrain()
    {
        await _heartbeat.RenewAsync();

        Assert.That(_grains.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task RenewAsync_ATabWhoseCatTheRoomLost_IsToldAndNoLongerRenewedThere()
    {
        World("tabby");
        var square = Room("tabby/town-square", lost: "tom-tab");
        _heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/town-square"));
        _heartbeat.Track("felix-tab", new(Felix, "tabby", "tabby/town-square"));

        await _heartbeat.RenewAsync();

        await _hub.Groups.Received(1).RemoveFromGroupAsync("tom-tab", "tabby/town-square");
        await _hub.Clients.Client("tom-tab").Received(1).RoomLost();
        _hub.Clients.DidNotReceive().Client("felix-tab");
        Assert.That(_heartbeat.PlaceOf("tom-tab"), Is.EqualTo(new ConnectionPlace(Tom, "tabby", null)), "still in the world");

        square.ClearReceivedCalls();
        await _heartbeat.RenewAsync();
        await square.Received(1).Renew(Arg.Is<IReadOnlyList<PlayerSession>>(sessions => Same(sessions,
            new PlayerSession(Felix, "felix-tab"))));
    }

    [Test]
    public async Task RenewAsync_ALostRoomTheTabAlreadyLeft_DoesNotTellIt()
    {
        World("tabby");
        var square = Room("tabby/town-square");
        _heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/town-square"));
        // The tab walks to the café while the square's answer is on its way.
        square.Renew(default!).ReturnsForAnyArgs(_ =>
        {
            _heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/cafe"));
            return Task.FromResult<IReadOnlyList<string>>(["tom-tab"]);
        });

        await _heartbeat.RenewAsync();

        _hub.Clients.DidNotReceiveWithAnyArgs().Client(default!);
        Assert.That(_heartbeat.PlaceOf("tom-tab")?.RoomKey, Is.EqualTo("tabby/cafe"));
    }

    [Test]
    public async Task Track_AfterTheConnectionClosed_IsRefused_SoItIsNeverRenewed()
    {
        var tabby = World("tabby");
        _heartbeat.Track("tom-tab", new(Tom, "tabby", null));

        Assert.That(_heartbeat.Close("tom-tab"), Is.EqualTo(new ConnectionPlace(Tom, "tabby", null)));
        Assert.That(_heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/town-square")), Is.False);

        Assert.That(_heartbeat.PlaceOf("tom-tab"), Is.Null);
        await _heartbeat.RenewAsync();
        await tabby.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task RenewAsync_ATabItsFullWorldWontTakeBack_LeavesItsRoomAndIsSentToPickAWorld()
    {
        World("tabby", refused: "tom-tab");
        var square = Room("tabby/town-square");
        _heartbeat.Track("tom-tab", new(Tom, "tabby", "tabby/town-square"));
        _heartbeat.Track("felix-tab", new(Felix, "tabby", "tabby/town-square"));

        await _heartbeat.RenewAsync();

        await _hub.Clients.Client("tom-tab").Received(1).WorldLost(Arg.Is<string>(m => m.Contains("Tabby", StringComparison.Ordinal)));
        await _hub.Groups.Received(1).RemoveFromGroupAsync("tom-tab", "tabby/town-square");
        await square.Received(1).Leave(Tom, "tom-tab");
        Assert.That(_heartbeat.PlaceOf("tom-tab"), Is.Null, "nowhere: it picks a world again");
        Assert.That(_heartbeat.PlaceOf("felix-tab"), Is.Not.Null);
        _hub.Clients.DidNotReceive().Client("felix-tab");
    }

    private T GrainFor<T>(string key)
        where T : class, IGrainWithStringKey
    {
        var grain = Substitute.For<T>();
        _grains.GetGrain<T>(key, null).Returns(grain);
        return grain;
    }

    private IWorldGrain World(string key, params string[] refused)
    {
        var world = GrainFor<IWorldGrain>(key);
        world.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>(refused));
        return world;
    }

    private IRoomGrain Room(string key, params string[] lost)
    {
        var room = GrainFor<IRoomGrain>(key);
        room.Renew(default!).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<string>>(lost));
        return room;
    }

    private static bool Same(IReadOnlyList<PlayerSession> actual, params PlayerSession[] expected) =>
        actual.Count == expected.Length && actual.ToHashSet().SetEquals(expected);
}
