using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

public sealed class ModerationSignalTests
{
    private static readonly Guid Felix = Guid.NewGuid();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private IModerationService _moderation = null!;
    private ServiceProvider _services = null!;
    private PlayerConnections _connections = null!;
    private StandingCache _standings = null!;
    private ModerationOnThisPod _pod = null!;

    [SetUp]
    public void Create()
    {
        _moderation = Substitute.For<IModerationService>();
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _services = new ServiceCollection().AddScoped(_ => _moderation).BuildServiceProvider();
        _connections = new PlayerConnections();
        _standings = new StandingCache(_services.GetRequiredService<IServiceScopeFactory>(), _time);
        _pod = new ModerationOnThisPod(_connections, _standings, _time);
    }

    [TearDown]
    public void Dispose() => _services.Dispose();

    [Test]
    public void ABanFromAnotherPod_ClosesThePlayersConnectionsHere_AfterTheirTabsHadTimeToHearWhy()
    {
        var tab = Tab("felix-tab", Felix);
        var other = Tab("tom-tab", Guid.NewGuid());

        _pod.Apply(Felix, close: true, decidedHere: false);
        tab.DidNotReceive().Abort();
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
        other.DidNotReceive().Abort();
    }

    [Test]
    public async Task AChangeFromAnotherPod_IsReadAgainHere_AtThePlayersNextCall()
    {
        await _standings.BanOfAsync(Felix, default);
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _moderation.StandingAsync(Felix, Arg.Any<CancellationToken>()).Returns(new Standing(ban, null));

        _pod.Apply(Felix, close: false, decidedHere: false);

        Assert.That(await _standings.BanOfAsync(Felix, default), Is.EqualTo(ban));
    }

    [Test]
    public async Task AChangeDecidedHere_KeepsWhatThisPodKnows_WithoutReadingAgain()
    {
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _standings.Know(Felix, new Standing(ban, null));

        _pod.Apply(Felix, close: false, decidedHere: true);

        Assert.That(await _standings.BanOfAsync(Felix, default), Is.EqualTo(ban));
        await _moderation.DidNotReceiveWithAnyArgs().StandingAsync(default, default);
    }

    [Test]
    public async Task OnOnePod_TheSignalApplies_Here()
    {
        var tab = Tab("felix-tab", Felix);

        await new LocalModerationSignal(_pod).SendAsync(Felix, close: true);
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public async Task ARedisSignal_AppliesLocally_WithoutWaitingForItsOwnMessageBack()
    {
        // No Redis to connect to: the publish fails, and the banning pod must still close its own connections right
        // away, rather than only if its own pub/sub message happens to come back to it.
        var tab = Tab("felix-tab", Felix);
        var signal = new RedisModerationSignal("localhost:0", _pod, NullLogger<RedisModerationSignal>.Instance);

        await signal.SendAsync(Felix, close: true);
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public async Task ARedisSignal_IgnoresItsOwnMessageComingBack_SoItIsntAppliedTwice()
    {
        var tab = Tab("felix-tab", Felix);
        var signal = new RedisModerationSignal("localhost:0", _pod, NullLogger<RedisModerationSignal>.Instance);

        await signal.SendAsync(Felix, close: true);
        // What would come back over the subscription had Redis been reachable: ignored, since this pod already applied
        // it when it sent it. Applying it again would start a second close timer.
        signal.Receive(new ModerationMessage(signal.SelfId, Felix, Close: true).Format());
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public void AMessage_ReadsBackAsItWasSent()
    {
        var sent = new ModerationMessage("pod-a", Felix, Close: true);

        Assert.That(ModerationMessage.Parse(sent.Format()), Is.EqualTo(sent));
        Assert.That(ModerationMessage.Parse((sent with { Close = false }).Format()), Is.EqualTo(sent with { Close = false }));
    }

    [Test]
    public void AMessageFromANewerPod_WithMoreFields_IsStillRead()
    {
        Assert.That(ModerationMessage.Parse($"1|pod-b|{Felix}|1|something-new"), Is.EqualTo(new ModerationMessage("pod-b", Felix, true)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("hello")]
    [TestCase("2|pod-a|0196d5a8-6b1e-7c3a-9f00-1234567890ab|1")]
    [TestCase("1||0196d5a8-6b1e-7c3a-9f00-1234567890ab|1")]
    [TestCase("1|pod-a|not-a-player|1")]
    [TestCase("1|pod-a|0196d5a8-6b1e-7c3a-9f00-1234567890ab|yes")]
    [TestCase("1|pod-a|0196d5a8-6b1e-7c3a-9f00-1234567890ab")]
    public void WhatIsntAMessage_IsNotRead(string? text)
    {
        Assert.That(ModerationMessage.Parse(text), Is.Null);
    }

    private HubCallerContext Tab(string connectionId, Guid playerId)
    {
        var tab = Substitute.For<HubCallerContext>();
        tab.ConnectionId.Returns(connectionId);
        _connections.Add(playerId, tab);
        return tab;
    }
}
