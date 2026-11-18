using Microsoft.EntityFrameworkCore;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

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
}
