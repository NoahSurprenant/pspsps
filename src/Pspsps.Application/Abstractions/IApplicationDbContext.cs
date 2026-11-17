using Microsoft.EntityFrameworkCore;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Abstractions;

/// <summary>The database as the Application sees it; Infrastructure implements it with EF Core.</summary>
public interface IApplicationDbContext
{
    DbSet<Player> Players { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether a save failed because a row with the same unique key is already there (another request won).</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>
    /// Stops tracking the player and their logins, so their unsaved changes (refused by the database) aren't saved by
    /// the next save. Nothing else in the scope is touched.
    /// </summary>
    void Forget(Player player);
}
