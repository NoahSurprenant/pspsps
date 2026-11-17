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
}
