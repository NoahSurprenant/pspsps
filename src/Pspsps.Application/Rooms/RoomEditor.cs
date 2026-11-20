using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

public sealed record RoomSummary(string RoomId, string Name, int? Published, int? Draft);

public sealed record VersionSummary(int Number, RoomVersionState State, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record RoomDetail(
    string RoomId,
    IReadOnlyList<VersionSummary> Versions,
    RoomLayout? Published,
    RoomLayout? Draft,
    IReadOnlyList<RoomOverlay> Overlays);

/// <summary>What admins do to rooms: draft, check, publish and roll back layouts, and set overlays. Every change is checked as a whole book first.</summary>
public interface IRoomEditor
{
    Task<IReadOnlyList<RoomSummary>> ListAsync(CancellationToken cancellationToken);

    Task<RoomDetail?> GetAsync(string roomId, CancellationToken cancellationToken);

    /// <summary>Keeps the layout as the room's draft (a new room's first one too) and says what would stop it being published.</summary>
    Task<IReadOnlyList<string>> SaveDraftAsync(string roomId, RoomLayout layout, Guid adminId, CancellationToken cancellationToken);

    Task DiscardDraftAsync(string roomId, CancellationToken cancellationToken);

    /// <summary>Publishes the draft in place of the published version; throws <see cref="DomainException"/> with the problems if the rooms together would be wrong.</summary>
    Task PublishAsync(string roomId, CancellationToken cancellationToken);

    /// <summary>Makes a draft of an earlier version, and publishes it.</summary>
    Task RollbackAsync(string roomId, int number, Guid adminId, CancellationToken cancellationToken);

    Task SaveOverlayAsync(RoomOverlay overlay, CancellationToken cancellationToken);

    Task DeleteOverlayAsync(string roomId, string overlayId, CancellationToken cancellationToken);
}

public sealed class RoomEditor(IApplicationDbContext db, IRoomLibrary library, TimeProvider time) : IRoomEditor
{
    public async Task<IReadOnlyList<RoomSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var versions = await db.RoomVersions.AsNoTracking().ToListAsync(cancellationToken);
        return [.. versions
            .GroupBy(v => v.RoomId)
            .OrderBy(group => group.Key == RoomBook.StartingRoom ? 0 : 1).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var published = group.FirstOrDefault(v => v.State == RoomVersionState.Published);
                var draft = group.FirstOrDefault(v => v.State == RoomVersionState.Draft);
                return new RoomSummary(group.Key, (published ?? draft ?? group.First()).Layout.Name, published?.Number, draft?.Number);
            })];
    }

    public async Task<RoomDetail?> GetAsync(string roomId, CancellationToken cancellationToken)
    {
        var versions = await db.RoomVersions.AsNoTracking().Where(v => v.RoomId == roomId).OrderByDescending(v => v.Number).ToListAsync(cancellationToken);
        if (versions.Count == 0)
        {
            return null;
        }

        var overlays = await db.RoomOverlays.AsNoTracking().Where(o => o.RoomId == roomId).ToListAsync(cancellationToken);
        return new RoomDetail(
            roomId,
            [.. versions.Select(v => new VersionSummary(v.Number, v.State, v.CreatedAt, v.PublishedAt))],
            versions.FirstOrDefault(v => v.State == RoomVersionState.Published)?.Layout,
            versions.FirstOrDefault(v => v.State == RoomVersionState.Draft)?.Layout,
            [.. overlays.Select(o => o.ToOverlay()).OrderBy(o => o.Id, StringComparer.Ordinal)]);
    }

    public async Task<IReadOnlyList<string>> SaveDraftAsync(string roomId, RoomLayout layout, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var versions = await db.RoomVersions.Where(v => v.RoomId == roomId).ToListAsync(cancellationToken);
        var draft = versions.FirstOrDefault(v => v.State == RoomVersionState.Draft);
        if (draft is null)
        {
            db.RoomVersions.Add(RoomVersion.Draft(roomId, versions.Select(v => v.Number).DefaultIfEmpty(0).Max() + 1, layout, adminId, time.GetUtcNow()));
        }
        else
        {
            draft.Edit(layout);
        }

        await SaveAsync(cancellationToken);
        return await ProblemsWithAsync(roomId, layout, cancellationToken);
    }

    public async Task DiscardDraftAsync(string roomId, CancellationToken cancellationToken)
    {
        var draft = await db.RoomVersions.FirstOrDefaultAsync(v => v.RoomId == roomId && v.State == RoomVersionState.Draft, cancellationToken)
            ?? throw new DomainException("This room has no draft.");
        db.RoomVersions.Remove(draft);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishAsync(string roomId, CancellationToken cancellationToken)
    {
        var versions = await db.RoomVersions.Where(v => v.RoomId == roomId).ToListAsync(cancellationToken);
        var draft = versions.FirstOrDefault(v => v.State == RoomVersionState.Draft) ?? throw new DomainException("This room has no draft.");
        await PublishAsync(versions, draft, cancellationToken);
    }

    public async Task RollbackAsync(string roomId, int number, Guid adminId, CancellationToken cancellationToken)
    {
        var versions = await db.RoomVersions.Where(v => v.RoomId == roomId).ToListAsync(cancellationToken);
        var old = versions.FirstOrDefault(v => v.Number == number) ?? throw new DomainException($"{roomId} has no version {number}.");
        var existing = versions.FirstOrDefault(v => v.State == RoomVersionState.Draft);
        if (existing is not null)
        {
            db.RoomVersions.Remove(existing);
            versions.Remove(existing);
        }

        var back = old.DraftFrom(versions.Max(v => v.Number) + 1, adminId, time.GetUtcNow());
        db.RoomVersions.Add(back);
        await PublishAsync(versions, back, cancellationToken);
    }

    public async Task SaveOverlayAsync(RoomOverlay overlay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var published = await PublishedLayoutsAsync(cancellationToken);
        var overlays = (await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken))
            .Select(entry => entry.ToOverlay())
            .Where(o => o.RoomId != overlay.RoomId || o.Id != overlay.Id)
            .Append(overlay)
            .ToList();
        ThrowIfProblems(RoomBook.Problems(published, overlays));

        var entry = await db.RoomOverlays.FirstOrDefaultAsync(o => o.RoomId == overlay.RoomId && o.Id == overlay.Id, cancellationToken);
        if (entry is null)
        {
            db.RoomOverlays.Add(RoomOverlayEntry.Of(overlay));
        }
        else
        {
            entry.Replace(overlay);
        }

        await SaveAsync(cancellationToken);
        await library.RefreshAsync(cancellationToken);
    }

    public async Task DeleteOverlayAsync(string roomId, string overlayId, CancellationToken cancellationToken)
    {
        var entry = await db.RoomOverlays.FirstOrDefaultAsync(o => o.RoomId == roomId && o.Id == overlayId, cancellationToken)
            ?? throw new DomainException($"{roomId} has no overlay {overlayId}.");
        db.RoomOverlays.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        await library.RefreshAsync(cancellationToken);
    }

    private async Task PublishAsync(List<RoomVersion> versions, RoomVersion draft, CancellationToken cancellationToken)
    {
        ThrowIfProblems(await ProblemsWithAsync(draft.RoomId, draft.Layout, cancellationToken));
        var current = versions.FirstOrDefault(v => v.State == RoomVersionState.Published);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (current is not null)
            {
                current.Archive();
                await db.SaveChangesAsync(cancellationToken);
            }

            draft.Publish(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            throw new DomainException("Someone else changed this room at the same moment: look again and retry.");
        }

        await library.RefreshAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            throw new DomainException("Someone else changed this room at the same moment: look again and retry.");
        }
    }

    private async Task<Dictionary<string, RoomLayout>> PublishedLayoutsAsync(CancellationToken cancellationToken) =>
        await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Published)
            .ToDictionaryAsync(v => v.RoomId, v => v.Layout, cancellationToken);

    private async Task<IReadOnlyList<string>> ProblemsWithAsync(string roomId, RoomLayout layout, CancellationToken cancellationToken)
    {
        var layouts = await PublishedLayoutsAsync(cancellationToken);
        layouts[roomId] = layout;
        var overlays = (await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken)).Select(entry => entry.ToOverlay()).ToList();
        return RoomBook.Problems(layouts, overlays);
    }

    private static void ThrowIfProblems(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }
    }
}
