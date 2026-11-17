using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

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
        // A network that drops packets leaves a connect hanging; each attempt must stop at its own timeout. The connect
        // waits on its token, and the fake clock moves past the attempt's timeout while it hangs.
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var reported = new List<Exception>();

        var reached = await DatabaseStartup.WaitAsync(
            ct =>
            {
                attempts++;
                var hanging = Task.Delay(Timeout.InfiniteTimeSpan, ct);
                clock.Advance(Options.AttemptTimeout);
                return hanging;
            },
            _ => false, (_, e) => reported.Add(e), Options, clock, Advance(clock), CancellationToken.None);

        Assert.That(reached, Is.False);
        // 5 s attempts 2 s apart: the 14th starts at 90 s and ends at 95 s, one attempt timeout past the budget.
        Assert.That(attempts, Is.EqualTo(14));
        Assert.That(clock.GetUtcNow() - clock.Start, Is.EqualTo(Options.Budget + Options.AttemptTimeout));
        Assert.That(reported, Has.Count.EqualTo(attempts).And.All.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task WaitAsync_ReportsARealErrorAsItselfEvenAfterTheAttemptTimedOut()
    {
        // The connect failed on its own just as its timeout fired: its real error is what's reported.
        var clock = new FakeTimeProvider();
        var refused = new InvalidOperationException("Connection refused");
        var reported = new List<Exception>();
        var attempts = 0;

        await DatabaseStartup.WaitAsync(
            _ =>
            {
                if (++attempts > 1)
                {
                    return Task.CompletedTask;
                }

                clock.Advance(Options.AttemptTimeout);
                throw refused;
            },
            _ => false, (_, e) => reported.Add(e), Options, clock, Advance(clock), CancellationToken.None);

        Assert.That(reported, Is.EqualTo(new Exception[] { refused }));
    }

    [TestCase("57P03", false, TestName = "IsPostgresAnswer_StartingUpIsNotYet")]
    [TestCase("57P01", false, TestName = "IsPostgresAnswer_ShuttingDownIsNotYet")]
    [TestCase("53300", false, TestName = "IsPostgresAnswer_TooManyConnectionsIsNotYet")]
    [TestCase("3D000", true, TestName = "IsPostgresAnswer_AMissingDatabaseIsAnAnswer")]
    [TestCase("28P01", true, TestName = "IsPostgresAnswer_AWrongPasswordIsAnAnswer")]
    public void IsPostgresAnswer_CountsOnlyNonTransientServerErrors(string sqlState, bool answer)
    {
        var error = new PostgresException("message", "FATAL", "FATAL", sqlState);

        Assert.That(DatabaseStartup.IsPostgresAnswer(error), Is.EqualTo(answer));
    }

    [Test]
    public void IsPostgresAnswer_ANetworkErrorIsNotYet()
    {
        var error = new NpgsqlException(
            "Failed to connect to 127.0.0.1:5432", new SocketException((int)SocketError.ConnectionRefused));

        Assert.That(DatabaseStartup.IsPostgresAnswer(error), Is.False);
    }

    [Test]
    public void Describe_GivesTheWholeChainDownToTheCause()
    {
        var cause = new SocketException((int)SocketError.ConnectionRefused);
        var error = new NpgsqlException("Failed to connect to 127.0.0.1:5432", cause);

        Assert.That(
            DatabaseStartup.Describe(error), Is.EqualTo($"Failed to connect to 127.0.0.1:5432 -> {cause.Message}"));
    }

    [Test]
    public void CheckConnectionString_DoesNotPoolTheCheck()
    {
        var check = new NpgsqlConnectionStringBuilder(
            DatabaseStartup.CheckConnectionString("Host=db;Database=pspsps;Username=pspsps;Password=secret"));

        Assert.That(check.Pooling, Is.False);
        Assert.That(check.Host, Is.EqualTo("db"));
        Assert.That(check.Database, Is.EqualTo("pspsps"));
    }

    [TestCase(null, 90)]
    [TestCase("1", 1)]
    [TestCase("120", 120)]
    public void OptionsFrom_ReadsTheBudget(string? configured, int seconds)
    {
        string? invalid = null;

        var options = DatabaseStartup.OptionsFrom(Configuration(configured), value => invalid = value);

        Assert.That(options.Budget, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
        Assert.That(options.Interval, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(options.AttemptTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(invalid, Is.Null);
    }

    [TestCase("0")]
    [TestCase("121")]
    [TestCase("-5")]
    [TestCase("ninety")]
    [TestCase("")]
    public void OptionsFrom_FallsBackToTheDefaultOnABadValue(string configured)
    {
        string? invalid = null;

        var options = DatabaseStartup.OptionsFrom(Configuration(configured), value => invalid = value);

        Assert.That(options.Budget, Is.EqualTo(TimeSpan.FromSeconds(90)));
        Assert.That(invalid, Is.EqualTo(configured));
    }

    private static IConfiguration Configuration(string? startupWaitSeconds) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:StartupWaitSeconds"] = startupWaitSeconds })
        .Build();

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
