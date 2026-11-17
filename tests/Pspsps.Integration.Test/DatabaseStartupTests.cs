using Npgsql;
using Pspsps.Server;

namespace Pspsps.Integration.Test;

// The server's own wait for PostgreSQL (the connection and how its errors are sorted), against the test container.
public sealed class DatabaseStartupTests
{
    [Test]
    public async Task WaitForPostgres_ReachesTheDatabase()
    {
        var failures = new List<Exception>();

        var reached = await DatabaseStartup.WaitForPostgresAsync(
            PostgresFixture.ConnectionString, Options(TimeSpan.FromSeconds(30)), TimeProvider.System,
            (_, e) => failures.Add(e), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public async Task WaitForPostgres_CountsAMissingDatabaseAsAnAnswer()
    {
        // PostgreSQL answers 3D000, which isn't transient: the migration should run now and create the database.
        var missing = new NpgsqlConnectionStringBuilder(PostgresFixture.ConnectionString) { Database = "no_such_db" };
        var failures = new List<Exception>();
        var started = TimeProvider.System.GetTimestamp();

        var reached = await DatabaseStartup.WaitForPostgresAsync(
            missing.ConnectionString, Options(TimeSpan.FromSeconds(30)), TimeProvider.System,
            (_, e) => failures.Add(e), CancellationToken.None);

        Assert.That(reached, Is.True);
        Assert.That(failures, Is.Empty);
        Assert.That(TimeProvider.System.GetElapsedTime(started), Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task WaitForPostgres_CountsAnUnreachablePortAsNotYetUntilTheBudgetEnds()
    {
        // Nothing listens on port 1: every connect is refused, and each refusal is reported.
        var unreachable = new NpgsqlConnectionStringBuilder(PostgresFixture.ConnectionString)
        {
            Host = "127.0.0.1",
            Port = 1,
        };
        var failures = new List<Exception>();

        var reached = await DatabaseStartup.WaitForPostgresAsync(
            unreachable.ConnectionString, Options(TimeSpan.FromSeconds(1)), TimeProvider.System,
            (_, e) => failures.Add(e), CancellationToken.None);

        Assert.That(reached, Is.False);
        Assert.That(failures, Is.Not.Empty.And.None.InstanceOf<PostgresException>());
    }

    private static DatabaseWaitOptions Options(TimeSpan budget) =>
        new(budget, Interval: TimeSpan.FromMilliseconds(100), AttemptTimeout: TimeSpan.FromSeconds(5));
}
