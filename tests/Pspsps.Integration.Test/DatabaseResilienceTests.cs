using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

public sealed class DatabaseResilienceTests
{
    private static GameFactory Game => PostgresFixture.Game;

    // A new pod's first connections can be refused for a moment (its network policy not in place yet, PostgreSQL
    // restarting). EF Core's execution strategy wraps every query, SaveChanges and the startup migration: it must retry,
    // or the server crashes at startup on one refused connection.
    [Test]
    public void TheDatabaseRetriesTransientFailures()
    {
        var db = Game.Get<PspspsDbContext>();

        Assert.That(db.Database.CreateExecutionStrategy().RetriesOnFailure, Is.True);
    }

    // A network that drops packets makes each connect wait its whole timeout: 5 s, not Npgsql's 15 s, keeps the
    // migration's retries inside the server's startup probe.
    [Test]
    public void TheDatabaseGivesUpConnectingAfterFiveSeconds()
    {
        var db = Game.Get<PspspsDbContext>();

        Assert.That(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Timeout, Is.EqualTo(5));
    }

    [Test]
    public void AConnectTimeoutInTheConnectionStringIsKept()
    {
        var connectionString = PostgresConnectionStrings.WithConnectDefaults("Host=db;Database=pspsps;Timeout=30");

        Assert.That(new NpgsqlConnectionStringBuilder(connectionString).Timeout, Is.EqualTo(30));
    }
}
