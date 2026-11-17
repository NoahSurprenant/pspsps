using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;

namespace Pspsps.Server.Test.Hubs;

public sealed class SessionHeartbeatTests
{
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly Guid Garfield = Guid.NewGuid();

    // NUnit runs every test on one instance of this class: a fresh factory each time.
    private IGrainFactory _grains = null!;

    [SetUp]
    public void NewGrainFactory() => _grains = Substitute.For<IGrainFactory>();

    [Test]
    public async Task RenewAsync_RenewsEachWorldAndRoomOnce_WithTheConnectionsInIt()
    {
        var tabby = GrainFor<IWorldGrain>("tabby");
        var square = GrainFor<IRoomGrain>("tabby/town-square");
        var cafe = GrainFor<IRoomGrain>("tabby/cafe");
        var heartbeat = new SessionHeartbeat(_grains, TimeProvider.System, NullLogger<SessionHeartbeat>.Instance);

        heartbeat.Track("tom-tab", Tom, "tabby", "tabby/town-square");
        heartbeat.Track("felix-tab", Felix, "tabby", "tabby/town-square");
        // In the world, on the way between rooms.
        heartbeat.Track("garfield-tab", Garfield, "tabby", null);
        // Was in the café, then left the world.
        heartbeat.Track("gone-tab", Guid.NewGuid(), "tabby", "tabby/cafe");
        heartbeat.Track("gone-tab", Guid.NewGuid(), null, null);

        await heartbeat.RenewAsync();

        await tabby.Received(1).Renew(Arg.Is<IReadOnlyList<PlayerSession>>(sessions => Same(sessions,
            new PlayerSession(Tom, "tom-tab"), new PlayerSession(Felix, "felix-tab"), new PlayerSession(Garfield, "garfield-tab"))));
        await square.Received(1).Renew(Arg.Is<IReadOnlyList<PlayerSession>>(sessions => Same(sessions,
            new PlayerSession(Tom, "tom-tab"), new PlayerSession(Felix, "felix-tab"))));
        await cafe.DidNotReceiveWithAnyArgs().Renew(default!);
    }

    [Test]
    public async Task RenewAsync_WithNobodyHere_CallsNoGrain()
    {
        var heartbeat = new SessionHeartbeat(_grains, TimeProvider.System, NullLogger<SessionHeartbeat>.Instance);

        await heartbeat.RenewAsync();

        Assert.That(_grains.ReceivedCalls(), Is.Empty);
    }

    private T GrainFor<T>(string key)
        where T : class, IGrainWithStringKey
    {
        var grain = Substitute.For<T>();
        _grains.GetGrain<T>(key, null).Returns(grain);
        return grain;
    }

    private static bool Same(IReadOnlyList<PlayerSession> actual, params PlayerSession[] expected) =>
        actual.Count == expected.Length && actual.ToHashSet().SetEquals(expected);
}
