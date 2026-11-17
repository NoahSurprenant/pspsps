using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence;

public sealed class PspspsDbContext(DbContextOptions<PspspsDbContext> options)
    : DbContext(options), IApplicationDbContext, IDataProtectionKeyContext
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    /// <summary>The ASP.NET Core key ring, shared by every server pod so a sign-in cookie works on all of them.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public void Forget(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        // Copies: detaching an added login or item takes it out of the player's list.
        foreach (var login in player.Logins.ToList())
        {
            Entry(login).State = EntityState.Detached;
        }

        foreach (var item in player.Items.ToList())
        {
            Entry(item).State = EntityState.Detached;
        }

        Entry(player).State = EntityState.Detached;
    }

    public void Forget(Friendship friendship)
    {
        ArgumentNullException.ThrowIfNull(friendship);
        Entry(friendship).State = EntityState.Detached;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PspspsDbContext).Assembly);
}
