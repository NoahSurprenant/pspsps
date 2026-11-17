using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

/// <summary>The homes' migration, on a database of its own that had cats, and their things, before homes.</summary>
public sealed class HomesMigrationTests
{
    [Test]
    public async Task CatsFromBeforeHomes_HaveOneOfEachThing_AndABed()
    {
        var connection = new NpgsqlConnectionStringBuilder(PostgresFixture.ConnectionString)
        {
            Database = "homes_migration_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        await using var db = new PspspsDbContext(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117103334_ShopItems");
        var tom = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO players (id, display_name, fur_color, coins, is_guest, created_at, outfit_version)
            VALUES ({tom}, 'Tom', 'Grey', 12, true, now(), 0)
            """);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO player_items (player_id, item_id, at) VALUES ({tom}, 'fur-grey', now()), ({tom}, 'bow', now())
            """);

        await migrator.MigrateAsync();
        var items = await db.Database
            .SqlQuery<string>($"SELECT item_id || ' ' || count AS \"Value\" FROM player_items WHERE player_id = {tom}")
            .ToListAsync();
        var player = await db.Players.SingleAsync(p => p.Id == tom);

        Assert.That(items, Is.EquivalentTo(new[] { "fur-grey 1", "bow 1", "cat-bed 1" }));
        Assert.That(player.CountOf("cat-bed"), Is.EqualTo(1));
        Assert.That(await db.Homes.CountAsync(), Is.Zero, "a home is made the first time its owner changes it");
        await db.Database.EnsureDeletedAsync();
    }
}
