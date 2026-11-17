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

    /// <summary>Forgets every change not saved yet, so a refused one isn't saved by the next save.</summary>
    void DiscardChanges();
}
