using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;

namespace Pspsps.Infrastructure.Persistence;

public sealed class PspspsDbContext(DbContextOptions<PspspsDbContext> options)
    : DbContext(options), IApplicationDbContext, IDataProtectionKeyContext
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Home> Homes => Set<Home>();

    public DbSet<PlayerModeration> Moderation => Set<PlayerModeration>();

    public DbSet<ModerationEntry> ModerationEntries => Set<ModerationEntry>();

    public DbSet<PlayerAddress> PlayerAddresses => Set<PlayerAddress>();

    public DbSet<AddressBan> AddressBans => Set<AddressBan>();

    public DbSet<Mute> Mutes => Set<Mute>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<RoomVersion> RoomVersions => Set<RoomVersion>();

    public DbSet<RoomOverlayEntry> RoomOverlays => Set<RoomOverlayEntry>();

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

    public void Forget(PlayerModeration moderation)
    {
        ArgumentNullException.ThrowIfNull(moderation);
        // A copy: the lines it added are in its list, and nothing else of it is loaded.
        foreach (var entry in moderation.Entries.ToList())
        {
            Entry(entry).State = EntityState.Detached;
        }

        Entry(moderation).State = EntityState.Detached;
    }

    public void Forget(AddressBan ban)
    {
        ArgumentNullException.ThrowIfNull(ban);
        // Its new audit lines are tracked apart (added to ModerationEntries), not through it.
        foreach (var entry in ban.Entries)
        {
            Entry(entry).State = EntityState.Detached;
        }

        Entry(ban).State = EntityState.Detached;
    }

    public void Forget(PlayerAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Entry(address).State = EntityState.Detached;
    }

    public void Forget(Mute mute)
    {
        ArgumentNullException.ThrowIfNull(mute);
        Entry(mute).State = EntityState.Detached;
    }

    public void Forget(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var line in report.Context.ToList())
        {
            Entry(line).State = EntityState.Detached;
        }

        Entry(report).State = EntityState.Detached;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PspspsDbContext).Assembly);
}
