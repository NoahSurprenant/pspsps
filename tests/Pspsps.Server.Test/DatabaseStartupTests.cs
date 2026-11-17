using Microsoft.Extensions.Time.Testing;

namespace Pspsps.Server.Test;

public sealed class DatabaseStartupTests
{
    private static readonly DatabaseWaitOptions Options = new(
        Budget: TimeSpan.FromSeconds(90), Interval: TimeSpan.FromSeconds(2), AttemptTimeout: TimeSpan.FromSeconds(5));

    [Test]
    public async Task WaitAsync_ConnectsOnTheThirdAttempt()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var failures = new List<int>();

        var reached = await DatabaseStartup.WaitAsync(
            _ => ++attempts < 3 ? throw new InvalidOperationException("Connection refused") : Task.CompletedTask,
            _ => false, (attempt, _) => failures.Add(attempt), Options, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(failures, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(clock.GetUtcNow() - clock.Start, Is.EqualTo(Options.Interval * 2));
    }

    [Test]
    public async Task WaitAsync_CountsAnAnswerAsReachedAtOnce()
    {
        // The server answered, even with an error (a missing database, a wrong password): the migration should run
        // now and fail or succeed with that error, not after the whole budget.
        var clock = new FakeTimeProvider();
        var answer = new InvalidOperationException("database \"pspsps\" does not exist");
        var attempts = 0;
        var failures = 0;

        var reached = await DatabaseStartup.WaitAsync(
            _ =>
            {
                attempts++;
                throw answer;
            },
            e => ReferenceEquals(e, answer), (_, _) => failures++, Options, clock, Advance(clock),
            CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(failures, Is.Zero);
        Assert.That(clock.GetUtcNow(), Is.EqualTo(clock.Start));
    }

    [Test]
    public async Task WaitAsync_CountsAThrownNonAnswerAsNotYetAndReportsIt()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var reported = new List<Exception>();
        var refused = new InvalidOperationException("Connection refused");
        // A cancellation the caller didn't ask for (a connect timeout surfaced as one) is "not yet" too.
        var canceled = new TaskCanceledException("A connect timed out");

        var reached = await DatabaseStartup.WaitAsync(
            _ => ++attempts switch
            {
                1 => throw refused,
                2 => throw canceled,
                _ => Task.CompletedTask,
            },
            _ => false, (_, e) => reported.Add(e), Options, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(reported, Is.EqualTo(new Exception[] { refused, canceled }));
    }

    [Test]
    public async Task WaitAsync_GivesUpAtTheDeadlineOnTheMonotonicClock()
    {
        // The wall clock jumps a day ahead (an NTP step at boot); the deadline must not move with it.
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var reached = await DatabaseStartup.WaitAsync(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException("Connection refused");
            },
            _ => false, (_, _) => { }, Options, new SteppedWallClock(clock, TimeSpan.FromDays(1)), Advance(clock),
            CancellationToken.None);

        Assert.That(reached, Is.False);
        Assert.That(clock.GetUtcNow() - clock.Start, Is.EqualTo(Options.Budget));
        Assert.That(attempts, Is.EqualTo(46), "one at the start, then one every interval up to the deadline");
    }

    [Test]
    public async Task WaitAsync_CutsOffAHangingAttempt()
    {
        // A network that drops packets leaves a connect hanging; each attempt must stop at its own timeout. Real time
        // here, with tiny timeouts.
        var options = new DatabaseWaitOptions(
            Budget: TimeSpan.FromMilliseconds(300), Interval: TimeSpan.FromMilliseconds(10),
            AttemptTimeout: TimeSpan.FromMilliseconds(50));
        var attempts = 0;
        var reported = new List<Exception>();
        var started = TimeProvider.System.GetTimestamp();

        var reached = await DatabaseStartup.WaitAsync(
            ct =>
            {
                attempts++;
                return Task.Delay(Timeout.InfiniteTimeSpan, ct);
            },
            _ => false, (_, e) => reported.Add(e), options, TimeProvider.System,
            (delay, ct) => Task.Delay(delay, ct), CancellationToken.None);

        Assert.That(reached, Is.False);
        Assert.That(attempts, Is.GreaterThan(1));
        Assert.That(reported, Has.Count.EqualTo(attempts).And.All.InstanceOf<TimeoutException>());
        Assert.That(TimeProvider.System.GetElapsedTime(started), Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void WaitAsync_StopsWhenTheCallerCancels()
    {
        var clock = new FakeTimeProvider();
        using var stop = new CancellationTokenSource();
        var attempts = 0;

        Assert.That(
            async () => await DatabaseStartup.WaitAsync(
                async ct =>
                {
                    attempts++;
                    await stop.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                },
                _ => false, (_, _) => { }, Options, clock, Advance(clock), stop.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(attempts, Is.EqualTo(1));
    }

    // Waiting moves the fake clock instead of sleeping.
    private static Func<TimeSpan, CancellationToken, Task> Advance(FakeTimeProvider clock) => (delay, _) =>
    {
        clock.Advance(delay);
        return Task.CompletedTask;
    };

    // The inner clock's monotonic time and timers, with a wall clock that jumps by `step` after its first reading.
    private sealed class SteppedWallClock(FakeTimeProvider inner, TimeSpan step) : TimeProvider
    {
        private bool _read;

        public override DateTimeOffset GetUtcNow()
        {
            var now = _read ? inner.GetUtcNow() + step : inner.GetUtcNow();
            _read = true;
            return now;
        }

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
