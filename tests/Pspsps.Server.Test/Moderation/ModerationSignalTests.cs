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
    private const string Network = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private IModerationService _moderation = null!;
    private IAddressBanService _networkService = null!;
    private ServiceProvider _services = null!;
    private PlayerConnections _connections = null!;
    private StandingCache _standings = null!;
    private AddressBanCache _networks = null!;
    private ModerationOnThisPod _pod = null!;

    [SetUp]
    public void Create()
    {
        _moderation = Substitute.For<IModerationService>();
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _networkService = Substitute.For<IAddressBanService>();
        _services = new ServiceCollection().AddScoped(_ => _moderation).AddScoped(_ => _networkService).BuildServiceProvider();
        _connections = new PlayerConnections();
        _standings = new StandingCache(_services.GetRequiredService<IServiceScopeFactory>(), _time);
        _networks = new AddressBanCache(_services.GetRequiredService<IServiceScopeFactory>(), _time);
        _pod = new ModerationOnThisPod(_connections, _standings, _networks, _time);
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

    [Test]
    public void ANetworkBanFromAnotherPod_ClosesTheConnectionsOnItHere_AfterTheirTabsHadTimeToHearWhy()
    {
        var tab = TabOnNetwork("felix-tab", Felix, Network);
        var other = Tab("tom-tab", Guid.NewGuid());

        _pod.ApplyNetwork(Network, close: true, decidedHere: false);
        tab.DidNotReceive().Abort();
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
        other.DidNotReceive().Abort();
    }

    [Test]
    public async Task ANetworkChangeFromAnotherPod_IsReadAgainHere_AtItsNextCall()
    {
        await _networks.BanOfAsync(Network, default);
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _networkService.BanOfAsync(Network, Arg.Any<CancellationToken>()).Returns(ban);

        _pod.ApplyNetwork(Network, close: false, decidedHere: false);

        Assert.That(await _networks.BanOfAsync(Network, default), Is.EqualTo(ban));
    }

    [Test]
    public async Task ANetworkChangeDecidedHere_KeepsWhatThisPodKnows_WithoutReadingAgain()
    {
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _networks.Know(Network, ban);

        _pod.ApplyNetwork(Network, close: false, decidedHere: true);

        Assert.That(await _networks.BanOfAsync(Network, default), Is.EqualTo(ban));
        await _networkService.DidNotReceiveWithAnyArgs().BanOfAsync(default!, default);
    }

    [Test]
    public async Task OnOnePod_TheNetworkSignalApplies_Here()
    {
        var tab = TabOnNetwork("felix-tab", Felix, Network);

        await new LocalModerationSignal(_pod).SendNetworkAsync(Network, close: true);
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public async Task ARedisSignal_AppliesNetworkBansLocally_WithoutWaitingForItsOwnMessageBack()
    {
        var tab = TabOnNetwork("felix-tab", Felix, Network);
        var signal = new RedisModerationSignal("localhost:0", _pod, NullLogger<RedisModerationSignal>.Instance);

        await signal.SendNetworkAsync(Network, close: true);
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public async Task ARedisSignal_IgnoresItsOwnNetworkMessageComingBack_SoItIsntAppliedTwice()
    {
        var tab = TabOnNetwork("felix-tab", Felix, Network);
        var signal = new RedisModerationSignal("localhost:0", _pod, NullLogger<RedisModerationSignal>.Instance);

        await signal.SendNetworkAsync(Network, close: true);
        signal.Receive(new NetworkModerationMessage(signal.SelfId, Network, Close: true).Format());
        _time.Advance(ModerationOnThisPod.CloseAfter);

        tab.Received(1).Abort();
    }

    [Test]
    public void APlayerMessage_AndANetworkMessage_DontReadAsEachOther()
    {
        var player = new ModerationMessage("pod-a", Felix, Close: true);
        var network = new NetworkModerationMessage("pod-a", Network, Close: true);

        Assert.Multiple(() =>
        {
            Assert.That(ModerationMessage.Parse(network.Format()), Is.Null);
            Assert.That(NetworkModerationMessage.Parse(player.Format()), Is.Null);
        });
    }

    [Test]
    public void ANetworkMessage_ReadsBackAsItWasSent()
    {
        var sent = new NetworkModerationMessage("pod-a", Network, Close: true);

        Assert.That(NetworkModerationMessage.Parse(sent.Format()), Is.EqualTo(sent));
        Assert.That(NetworkModerationMessage.Parse((sent with { Close = false }).Format()), Is.EqualTo(sent with { Close = false }));
    }

    [Test]
    public void ANetworkMessageFromANewerPod_WithMoreFields_IsStillRead()
    {
        Assert.That(
            NetworkModerationMessage.Parse($"N1|pod-b|{Network}|1|something-new"),
            Is.EqualTo(new NetworkModerationMessage("pod-b", Network, true)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("hello")]
    [TestCase("N2|pod-a|" + Network + "|1")]
    [TestCase("N1||" + Network + "|1")]
    [TestCase("N1|pod-a|not-a-network|1")]
    [TestCase("N1|pod-a|" + Network + "|yes")]
    [TestCase("N1|pod-a|" + Network)]
    public void WhatIsntANetworkMessage_IsNotRead(string? text)
    {
        Assert.That(NetworkModerationMessage.Parse(text), Is.Null);
    }

    private HubCallerContext Tab(string connectionId, Guid playerId)
    {
        var tab = Substitute.For<HubCallerContext>();
        tab.ConnectionId.Returns(connectionId);
        _connections.Add(playerId, tab);
        return tab;
    }

    private HubCallerContext TabOnNetwork(string connectionId, Guid playerId, string addressHash)
    {
        var tab = Substitute.For<HubCallerContext>();
        tab.ConnectionId.Returns(connectionId);
        _connections.Add(playerId, tab, addressHash);
        return tab;
    }
}
