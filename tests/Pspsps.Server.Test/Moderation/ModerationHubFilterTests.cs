using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Server.Auth;
using Pspsps.Server.Hubs;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

public sealed class ModerationHubFilterTests
{
    private static readonly PlayerProfile Felix = new(Guid.NewGuid(), "Felix", FurColor.Black, 0, IsGuest: true);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private IModerationService _moderation = null!;
    private ServiceProvider _services = null!;
    private PlayerConnections _connections = null!;
    private ModerationHubFilter _filter = null!;

    [SetUp]
    public void Create()
    {
        _moderation = Substitute.For<IModerationService>();
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _services = new ServiceCollection().AddScoped(_ => _moderation).BuildServiceProvider();
        _connections = new PlayerConnections();
        _filter = new ModerationHubFilter(
            new StandingCache(_services.GetRequiredService<IServiceScopeFactory>(), _time), _connections);
    }

    [TearDown]
    public void Dispose() => _services.Dispose();

    [Test]
    public async Task AnyoneNotBanned_GetsThrough()
    {
        var called = false;

        await _filter.InvokeMethodAsync(Invocation(), _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(null);
        });

        Assert.That(called, Is.True);
    }

    [Test]
    public void ABannedPlayersCalls_AreRefused_WithWhyAndUntilWhen()
    {
        var caller = Caller("felix-tab");
        Ban(until: new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var called = false;

        var refused = Assert.ThrowsAsync<HubException>(async () => await _filter.InvokeMethodAsync(Invocation(caller), _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(null);
        }));

        Assert.That(refused!.Message, Is.EqualTo("Your cat is banned until 19 Nov 2026 12:00 UTC. Why: Spamming"));
        Assert.That(called, Is.False);
        // And the connection is closed, on whichever pod it is: a tab that ignored the ban gets nothing more.
        caller.Received(1).Abort();
    }

    [Test]
    public void ABannedPlayersConnection_IsRefusedAsItOpens_AndNotKept()
    {
        Ban(until: null);
        var connection = Caller("felix-tab");
        var opened = false;

        Assert.ThrowsAsync<HubException>(() => _filter.OnConnectedAsync(Lifetime(connection), _ =>
        {
            opened = true;
            return Task.CompletedTask;
        }));

        Assert.That(opened, Is.False);
        Assert.That(_connections.Of(Felix.Id), Is.Empty);
    }

    [Test]
    public async Task APlayersConnections_AreKeptWhileOpen()
    {
        var connection = Caller("felix-tab");

        await _filter.OnConnectedAsync(Lifetime(connection), _ => Task.CompletedTask);
        Assert.That(_connections.Of(Felix.Id), Is.EqualTo(new[] { connection }));

        await _filter.OnDisconnectedAsync(Lifetime(connection), null, (_, _) => Task.CompletedTask);
        Assert.That(_connections.Of(Felix.Id), Is.Empty);
    }

    private void Ban(DateTimeOffset? until) =>
        _moderation.StandingAsync(Felix.Id, Arg.Any<CancellationToken>())
            .Returns(new Standing(new Sanction("Spamming", _time.GetUtcNow(), until, Guid.NewGuid()), null));

    private static HubCallerContext Caller(string connectionId)
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.User.Returns(PlayerClaims.For(Felix));
        return context;
    }

    private static HubInvocationContext Invocation(HubCallerContext? caller = null) =>
        new(caller ?? Caller("felix-tab"), Substitute.For<IServiceProvider>(), Substitute.For<Hub>(),
            typeof(GameHub).GetMethod(nameof(GameHub.Say))!, ["hello"]);

    private static HubLifetimeContext Lifetime(HubCallerContext connection) =>
        new(connection, Substitute.For<IServiceProvider>(), Substitute.For<Hub>());
}
