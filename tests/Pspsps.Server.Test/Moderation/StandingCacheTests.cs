using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Application.Moderation;
using Pspsps.Domain;
using Pspsps.Domain.Moderation;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

public sealed class StandingCacheTests
{
    private static readonly Guid Felix = Guid.NewGuid();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private IModerationService _moderation = null!;
    private ServiceProvider _services = null!;
    private StandingCache _cache = null!;

    [SetUp]
    public void Create()
    {
        _moderation = Substitute.For<IModerationService>();
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _services = new ServiceCollection().AddScoped(_ => _moderation).BuildServiceProvider();
        _cache = new StandingCache(_services.GetRequiredService<IServiceScopeFactory>(), _time);
    }

    [TearDown]
    public void Dispose() => _services.Dispose();

    [Test]
    public async Task ReadsAPlayer_AtMostOnceEveryFewSeconds()
    {
        await _cache.BanOfAsync(Felix, default);
        _time.Advance(StandingCache.KeepFor - TimeSpan.FromMilliseconds(1));
        await _cache.BanOfAsync(Felix, default);
        await _moderation.Received(1).StandingAsync(Felix, Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await _cache.BanOfAsync(Felix, default);
        await _moderation.Received(2).StandingAsync(Felix, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AChangeOnThisPod_IsReadAtOnce()
    {
        Assert.That(await _cache.BanOfAsync(Felix, default), Is.Null);
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());

        _cache.Know(Felix, new Standing(ban, null));

        Assert.That(await _cache.BanOfAsync(Felix, default), Is.EqualTo(ban));
        await _moderation.Received(1).StandingAsync(Felix, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AChangeOnThisPod_HoldsWhenTheDatabaseCantAnswer()
    {
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _cache.Know(Felix, new Standing(ban, null));
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Task.FromException<Standing>(new TimeoutException()));
        _time.Advance(StandingCache.KeepFor);

        Assert.That(await _cache.BanOfAsync(Felix, default), Is.EqualTo(ban));
    }

    [Test]
    public async Task ABanRead_EndsAtItsTime_EvenBeforeItIsReadAgain()
    {
        var ban = new Sanction("Spam", _time.GetUtcNow(), _time.GetUtcNow().AddSeconds(2), Guid.NewGuid());
        _moderation.StandingAsync(Felix, Arg.Any<CancellationToken>()).Returns(new Standing(ban, null));
        Assert.That(await _cache.BanOfAsync(Felix, default), Is.EqualTo(ban));

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.That(await _cache.BanOfAsync(Felix, default), Is.Null);
        await _moderation.Received(1).StandingAsync(Felix, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AChangeToOnePlayer_DoesntThrowAwayAnotherPlayersRead()
    {
        var tom = Guid.NewGuid();
        var slow = new TaskCompletionSource<Standing>();
        _moderation.StandingAsync(tom, Arg.Any<CancellationToken>()).Returns(slow.Task);
        var reading = _cache.BanOfAsync(tom, default);
        _cache.Know(Felix, Standing.None);
        slow.SetResult(Standing.None);
        await reading;

        await _cache.BanOfAsync(tom, default);

        await _moderation.Received(1).StandingAsync(tom, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WhenTheDatabaseCantAnswer_TheLastReadHolds_AndAPlayerNeverReadPlays()
    {
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _moderation.StandingAsync(Felix, Arg.Any<CancellationToken>()).Returns(new Standing(ban, null));
        await _cache.BanOfAsync(Felix, default);
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Task.FromException<Standing>(new TimeoutException()));
        _time.Advance(StandingCache.KeepFor);

        Assert.That(await _cache.BanOfAsync(Felix, default), Is.EqualTo(ban), "a database restart doesn't lift a ban");
        Assert.That(await _cache.BanOfAsync(Guid.NewGuid(), default), Is.Null, "nor stop everyone else playing");
    }

    [Test]
    public async Task AMuteRead_IsTheChatRule_UntilItsTime()
    {
        var mute = new Sanction("Rude", _time.GetUtcNow(), _time.GetUtcNow().AddSeconds(2), Guid.NewGuid());
        _moderation.StandingAsync(Felix, Arg.Any<CancellationToken>()).Returns(new Standing(null, mute));

        var refused = Assert.ThrowsAsync<DomainException>(() => _cache.EnsureMayChatAsync(Felix, default));
        Assert.That(refused!.Message, Is.EqualTo(mute.SaysMuted()));

        // Over at its time, before the read is due again.
        _time.Advance(TimeSpan.FromSeconds(2));
        await _cache.EnsureMayChatAsync(Felix, default);
        await _moderation.Received(1).StandingAsync(Felix, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AReadThatStartedBeforeAChange_IsNotKept()
    {
        // The database answers slowly; the player is banned on this pod meanwhile.
        var slow = new TaskCompletionSource<Standing>();
        _moderation.StandingAsync(Felix, Arg.Any<CancellationToken>()).Returns(slow.Task);
        var reading = _cache.BanOfAsync(Felix, default);
        var ban = new Sanction("Spam", _time.GetUtcNow(), null, Guid.NewGuid());
        _cache.Know(Felix, new Standing(ban, null));
        slow.SetResult(Standing.None);
        await reading;

        Assert.That(await _cache.BanOfAsync(Felix, default), Is.EqualTo(ban), "the change, not the older read");
    }
}
