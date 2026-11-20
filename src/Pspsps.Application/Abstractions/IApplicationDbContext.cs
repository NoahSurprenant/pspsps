using Microsoft.EntityFrameworkCore;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Abstractions;

/// <summary>The database as the Application sees it; Infrastructure implements it with EF Core.</summary>
public interface IApplicationDbContext
{
    DbSet<Player> Players { get; }

    DbSet<Friendship> Friendships { get; }

    DbSet<Home> Homes { get; }

    /// <summary>Players' bans, mutes and warnings (one row per player an admin has acted on).</summary>
    DbSet<PlayerModeration> Moderation { get; }

    /// <summary>The audit trail of what admins did.</summary>
    DbSet<ModerationEntry> ModerationEntries { get; }

    /// <summary>The networks (keyed hashes, never addresses) each player played from in the last 7 days.</summary>
    DbSet<PlayerAddress> PlayerAddresses { get; }

    /// <summary>Banned networks, by keyed hash.</summary>
    DbSet<AddressBan> AddressBans { get; }

    DbSet<Mute> Mutes { get; }

    DbSet<Report> Reports { get; }

    /// <summary>Every version of every room: a room is what its published version says.</summary>
    DbSet<RoomVersion> RoomVersions { get; }

    /// <summary>The seasonal and event changes to rooms.</summary>
    DbSet<RoomOverlayEntry> RoomOverlays { get; }

    DbSet<PropDesign> PropDesigns { get; }

    DbSet<RoomLookEntry> RoomLooks { get; }

    DbSet<RoomAuditEntry> RoomAudit { get; }

    DbSet<LookVariantEntry> LookVariants { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction: its saves are kept together, or all dropped when it throws. The
    /// database may run it again after a dropped connection, so it must load what it changes itself, each time.
    /// </summary>
    Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    /// <summary>Whether a save failed because a row with the same unique key is already there (another request won).</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>
    /// Stops tracking the player, their logins and items, so their unsaved changes (refused by the database) aren't
    /// saved by the next save. Nothing else in the scope is touched.
    /// </summary>
    void Forget(Player player);

    /// <summary>Stops tracking the friendship, so its unsaved change (refused by the database) isn't saved by the next save.</summary>
    void Forget(Friendship friendship);

    /// <summary>Stops tracking the home and its furniture, so its unsaved changes (refused by the database) aren't saved.</summary>
    void Forget(Home home);

    /// <summary>Stops tracking the player's moderation and its new audit lines, so a refused change isn't saved.</summary>
    void Forget(PlayerModeration moderation);

    /// <summary>Stops tracking the network's ban and its new audit lines, so a refused change isn't saved.</summary>
    void Forget(AddressBan ban);

    /// <summary>Stops tracking the player's network, so a refused save of it isn't tried again.</summary>
    void Forget(PlayerAddress address);

    /// <summary>Stops tracking the mute, so its unsaved change (refused by the database) isn't saved by the next save.</summary>
    void Forget(Mute mute);

    /// <summary>Stops tracking the report and its lines, so its unsaved decision (refused by the database) isn't saved.</summary>
    void Forget(Report report);
}

