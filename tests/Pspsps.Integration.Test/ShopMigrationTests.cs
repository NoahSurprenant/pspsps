using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

/// <summary>The shop's migration, on a database of its own that had cats before the shop.</summary>
public sealed class ShopMigrationTests
{
    [Test]
    public async Task CatsFromBeforeTheShop_OwnTheCoatTheyHave()
    {
        var connection = new NpgsqlConnectionStringBuilder(PostgresFixture.ConnectionString)
        {
            Database = "shop_migration_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        await using var db = new PspspsDbContext(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117091027_Friendships");
        var tom = Guid.NewGuid();
        var felix = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO players (id, display_name, fur_color, coins, is_guest, created_at)
            VALUES ({tom}, 'Tom', 'Tuxedo', 12, true, now()), ({felix}, 'Felix', 'Orange', 0, false, now())
            """);

        await migrator.MigrateAsync();
        var items = await db.Database
            .SqlQuery<string>($"SELECT player_id::text || ' ' || item_id AS \"Value\" FROM player_items")
            .ToListAsync();

        Assert.That(items, Is.EquivalentTo(new[] { $"{tom} fur-tuxedo", $"{felix} fur-orange" }));
        var player = await db.Players.SingleAsync(p => p.Id == tom);
        Assert.That(player.Owns("fur-tuxedo"), Is.True);
        await db.Database.EnsureDeletedAsync();
    }
}
