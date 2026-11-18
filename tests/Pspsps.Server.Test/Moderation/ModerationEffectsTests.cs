using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

public sealed class ModerationEffectsTests
{
    private static readonly Guid Felix = Guid.NewGuid();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private IGameClient _felix = null!;
    private IGrainFactory _grains = null!;
    private IPlayerSessionGrain _session = null!;
    private PlayerConnections _connections = null!;
    private ServiceProvider _services = null!;
    private ModerationEffects _effects = null!;

    [SetUp]
    public void Create()
    {
        var hub = Substitute.For<IHubContext<GameHub, IGameClient>>();
        _felix = Substitute.For<IGameClient>();
        hub.Clients.User(Felix.ToString()).Returns(_felix);
        _grains = Substitute.For<IGrainFactory>();
        _session = Substitute.For<IPlayerSessionGrain>();
        _grains.GetGrain<IPlayerSessionGrain>(Felix, null).Returns(_session);
        _connections = new PlayerConnections();
        var moderation = Substitute.For<IModerationService>();
        moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _services = new ServiceCollection().AddScoped(_ => moderation).BuildServiceProvider();
        var standings = new StandingCache(_services.GetRequiredService<IServiceScopeFactory>(), _time);
        _effects = new ModerationEffects(hub, _connections, standings, _grains, _time, NullLogger<ModerationEffects>.Instance);
    }

    [TearDown]
    public void Dispose() => _services.Dispose();

    [Test]
    public async Task AWarnedPlayer_IsToldToFetchIt()
    {
        await _effects.AppliedAsync(Felix, ModerationAction.Warn, Standing.None);

        await _felix.Received(1).Warned();
    }

    [Test]
    public async Task AMutedPlayer_IsToldWhyAndUntilWhen_AndALiftedOne_ThatItsOver()
    {
        var until = _time.GetUtcNow().AddHours(1);
        await _effects.AppliedAsync(
            Felix, ModerationAction.Mute, new Standing(null, new Sanction("Rude", _time.GetUtcNow(), until, Guid.NewGuid())));
        await _felix.Received(1).Muted(new SanctionView("Rude", until));

        await _effects.AppliedAsync(Felix, ModerationAction.Lift, Standing.None);
        await _felix.Received(1).Muted(null);
    }

    [Test]
    public async Task ABannedPlayer_IsToldWhy_ThenTheirConnectionsClose_AndTheirCatLeavesItsRoom()
    {
        var tab = Substitute.For<HubCallerContext>();
        tab.ConnectionId.Returns("felix-tab");
        _connections.Add(Felix, tab);
        var room = Substitute.For<IRoomGrain>();
        _grains.GetGrain<IRoomGrain>("tabby/cafe", null).Returns(room);
        _session.Locate().Returns(new PlayerPlace("tabby", "cafe"));
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());

        await _effects.AppliedAsync(Felix, ModerationAction.Ban, new Standing(ban, null));

        await _felix.Received(1).Banned(new SanctionView("Spam", null));
        await room.Received(1).SendOut(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Felix })), ban.SaysBanned());
        tab.DidNotReceive().Abort();

        // The tab has a moment to hear why, then its connection closes.
        _time.Advance(ModerationEffects.CloseAfter);
        tab.Received(1).Abort();
    }

    [Test]
    public async Task ATabThatCantBeTold_StillHasItsConnectionClosed()
    {
        var tab = Substitute.For<HubCallerContext>();
        tab.ConnectionId.Returns("felix-tab");
        _connections.Add(Felix, tab);
        _felix.Banned(default!).ReturnsForAnyArgs(Task.FromException(new IOException("gone")));

        await _effects.AppliedAsync(
            Felix, ModerationAction.Ban, new Standing(new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid()), null));
        _time.Advance(ModerationEffects.CloseAfter);

        tab.Received(1).Abort();
    }
}
