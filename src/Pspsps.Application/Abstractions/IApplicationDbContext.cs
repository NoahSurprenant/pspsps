using Microsoft.EntityFrameworkCore;
using Pspsps.Domain.Players;

namespace Pspsps.Application.Abstractions;

/// <summary>The database as the Application sees it; Infrastructure implements it with EF Core.</summary>
public interface IApplicationDbContext
{
    DbSet<Player> Players { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
