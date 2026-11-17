using Microsoft.Extensions.Time.Testing;

namespace Pspsps.Server.Test;

public sealed class DatabaseStartupTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    [Test]
    public async Task WaitAsync_ReturnsAsSoonAsTheDatabaseAnswers()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var reached = await DatabaseStartup.WaitAsync(
            _ => Task.FromResult(++attempts == 3), Timeout, Interval, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(clock.GetUtcNow() - clock.Start, Is.EqualTo(Interval * 2));
    }

    [Test]
    public async Task WaitAsync_CountsAFailingAttemptAsNotYet()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var reached = await DatabaseStartup.WaitAsync(
            _ => ++attempts < 2 ? throw new InvalidOperationException("Connection refused") : Task.FromResult(true),
            Timeout, Interval, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task WaitAsync_GivesUpAtTheDeadline()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var reached = await DatabaseStartup.WaitAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            Timeout, Interval, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.False);
        Assert.That(clock.GetUtcNow() - clock.Start, Is.EqualTo(Timeout));
        Assert.That(attempts, Is.EqualTo(61), "one at the start, then one every interval up to the deadline");
    }

    // Waiting moves the fake clock instead of sleeping.
    private static Func<TimeSpan, CancellationToken, Task> Advance(FakeTimeProvider clock) => (delay, _) =>
    {
        clock.Advance(delay);
        return Task.CompletedTask;
    };
}
