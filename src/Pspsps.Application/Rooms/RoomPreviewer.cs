using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

/// <summary>A room as it would be, with every room beside it (for the map and the doors' names).</summary>
public sealed record RoomPreview(RoomDefinition Room, IReadOnlyList<RoomDefinition> All, RoomLook? DraftLook = null);

public interface IRoomPreviewer
{
    /// <summary>
    /// The room as it would be with every draft published (a draft's room, or the published one), and, when
    /// <paramref name="overlayId"/> is given, with that overlay on whatever its window says. Throws <see cref="DomainException"/>
    /// when the rooms together would be wrong, or there is no such room or overlay.
    /// </summary>
    Task<RoomPreview> PreviewAsync(string roomId, string? overlayId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class RoomPreviewer(IApplicationDbContext db) : IRoomPreviewer
{
    public async Task<RoomPreview> PreviewAsync(string roomId, string? overlayId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var versions = await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Published || v.State == RoomVersionState.Draft)
            .ToListAsync(cancellationToken);
        var layouts = new Dictionary<string, RoomLayout>();
        foreach (var version in versions.OrderBy(v => v.State == RoomVersionState.Draft ? 1 : 0))
        {
            layouts[version.RoomId] = version.Layout;
        }

        var draftLook = versions.FirstOrDefault(v => v.RoomId == roomId && v.State == RoomVersionState.Draft)?.Look;

        if (!layouts.ContainsKey(roomId))
        {
            throw new DomainException($"There is no room {roomId}.");
        }

        var overlays = new List<RoomOverlay>();
        if (overlayId is not null)
        {
            // An overlay can only be saved once its room is published, so one waiting in the reviewed file is previewed from there.
            var saved = (await db.RoomOverlays.AsNoTracking().Where(o => o.RoomId == roomId).ToListAsync(cancellationToken))
                .Select(entry => entry.ToOverlay())
                .Concat(ContentFile.Embedded().OverlayList.Where(o => o.RoomId == roomId))
                .FirstOrDefault(o => o.Id == overlayId)
                ?? throw new DomainException($"{roomId} has no overlay {overlayId}.");
            overlays.Add(saved with { Window = new OverlayWindow(now.AddDays(-1), now.AddDays(1)) });
        }

        var book = RoomBook.Create(layouts, overlays);
        return new RoomPreview(book.Find(roomId, now) ?? throw new DomainException($"There is no room {roomId}."), book.At(now), draftLook);
    }
}
