using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence;

public sealed class PspspsDbContext(DbContextOptions<PspspsDbContext> options)
    : DbContext(options), IApplicationDbContext, IDataProtectionKeyContext
{
    public DbSet<Player> Players => Set<Player>();

    /// <summary>The ASP.NET Core key ring, shared by every server pod so a sign-in cookie works on all of them.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PspspsDbContext).Assembly);
}
