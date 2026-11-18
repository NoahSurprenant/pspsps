using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Players;
using Pspsps.Domain.Safety;

namespace Pspsps.Infrastructure.Persistence;

public sealed class PspspsDbContext(DbContextOptions<PspspsDbContext> options)
    : DbContext(options), IApplicationDbContext, IDataProtectionKeyContext
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Home> Homes => Set<Home>();

    public DbSet<Mute> Mutes => Set<Mute>();

    public DbSet<Report> Reports => Set<Report>();

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

    public void Forget(Home home)
    {
        ArgumentNullException.ThrowIfNull(home);
        // A copy, as for a player's items. A piece put away (deleted) isn't in the list: detached with the rest below.
        foreach (var piece in home.Furniture.ToList())
        {
            Entry(piece).State = EntityState.Detached;
        }

        foreach (var removed in ChangeTracker.Entries<PlacedFurniture>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            removed.State = EntityState.Detached;
        }

        Entry(home).State = EntityState.Detached;
    }

    public void Forget(Mute mute)
    {
        ArgumentNullException.ThrowIfNull(mute);
        Entry(mute).State = EntityState.Detached;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PspspsDbContext).Assembly);
}
