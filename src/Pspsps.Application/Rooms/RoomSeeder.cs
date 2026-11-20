using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

/// <summary>
/// Puts the rooms a new database starts with (<see cref="RoomCatalog"/>) in it as published versions. Only into a
/// database with no room versions at all: once rooms are content, what is in the database is the truth, and the
/// catalogue's changes don't reach it.
/// </summary>
public static class RoomSeeder
{
    public static async Task<bool> SeedAsync(IApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        if (await db.RoomVersions.AnyAsync(cancellationToken))
        {
            return false;
        }

        var now = time.GetUtcNow();
        foreach (var room in RoomCatalog.All)
        {
            db.RoomVersions.Add(RoomVersion.Published(room.Id, RoomLayout.Of(room), now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            // Another pod, starting at the same time, put them there first.
            return false;
        }
    }
}
