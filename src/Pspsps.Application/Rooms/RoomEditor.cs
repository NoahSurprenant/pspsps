using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

public sealed record RoomSummary(string RoomId, string Name, int? Published, int? Draft, DateTimeOffset? GoLiveAt = null);

public sealed record VersionSummary(int Number, RoomVersionState State, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record RoomDetail(
    string RoomId,
    IReadOnlyList<VersionSummary> Versions,
    RoomLayout? Published,
    RoomLayout? Draft,
    IReadOnlyList<RoomOverlay> Overlays,
    int? DraftRevision = null,
    DateTimeOffset? GoLiveAt = null,
    IReadOnlyList<string>? PendingOverlays = null);

/// <summary>Everything admins author, in the shape the files in the repository hold it, to commit and review.</summary>
public sealed record ContentExport(
    IReadOnlyList<ExportedRoom> Rooms,
    IReadOnlyList<RoomOverlay> Overlays,
    ExportedLooks Looks,
    IReadOnlyList<object> Painters,
    IReadOnlyList<object> Wildlife,
    IReadOnlyList<object> PropDesigns);

public sealed record ExportedRoom(string Id, RoomLayout Layout);

public sealed record ExportedLooks(IReadOnlyList<object> Rooms, IReadOnlyList<object> Variants);

public sealed record AuditLine(DateTimeOffset At, Guid? AdminId, string Action, string Target, string Detail);

/// <summary>What admins do to rooms: draft, check, publish and roll back layouts, and set overlays. Every change is checked as a whole book first.</summary>
public interface IRoomEditor
{
    Task<IReadOnlyList<RoomSummary>> ListAsync(CancellationToken cancellationToken);

    Task<RoomDetail?> GetAsync(string roomId, CancellationToken cancellationToken);

    /// <summary>The published rooms (the starting room first), their overlays and looks, the painter and wildlife recipes and the prop designs, in a stable order.</summary>
    Task<ContentExport> ExportAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads the content file: each room that differs from its published version becomes its draft (nothing goes live until it is
    /// published). Looks, variants and recipes that are new are saved (nothing uses them yet); ones that already exist and differ are
    /// not touched and are reported, because changing them would change live rooms.
    /// </summary>
    Task<ImportReport> ImportAsync(ContentFile file, Guid adminId, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps the layout as the room's draft (a new room's first one too) and says what would stop it being published.
    /// <paramref name="expectedRevision"/> is the draft's <see cref="RoomDetail.DraftRevision"/> as the admin opened it: when the
    /// draft has been saved since (or is gone), nothing is saved and a <see cref="DomainException"/> says so.
    /// </summary>
    Task<IReadOnlyList<string>> SaveDraftAsync(string roomId, RoomLayout layout, Guid adminId, int? expectedRevision, CancellationToken cancellationToken);

    Task DiscardDraftAsync(string roomId, Guid adminId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes the room's draft in place of the published version, together with the drafts of <paramref name="together"/> (a
    /// new room and the doors that lead to it are only fine as a set); throws <see cref="DomainException"/> with the problems if
    /// the rooms together would be wrong.
    /// </summary>
    Task PublishAsync(string roomId, IReadOnlyList<string> together, Guid adminId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the room's draft together with the drafts of <paramref name="together"/> as <see cref="PublishAsync"/> does, and sets
    /// them to go live at <paramref name="at"/> (a later time) by themselves: <see cref="PublishDueAsync"/> does it then.
    /// </summary>
    Task ScheduleAsync(string roomId, IReadOnlyList<string> together, DateTimeOffset at, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Takes the room's draft off the schedule; it stays a draft.</summary>
    Task UnscheduleAsync(string roomId, Guid adminId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes the drafts whose time has come, together. A set that would now be wrong (a room published or changed since) is
    /// not published: it is taken off the schedule, with the problems put in the audit trail. Returns how many rooms went live.
    /// </summary>
    Task<int> PublishDueAsync(CancellationToken cancellationToken);

    /// <summary>Makes a draft of an earlier version, and publishes it.</summary>
    Task RollbackAsync(string roomId, int number, Guid adminId, CancellationToken cancellationToken);

    Task SaveOverlayAsync(RoomOverlay overlay, Guid adminId, CancellationToken cancellationToken);

    Task DeleteOverlayAsync(string roomId, string overlayId, Guid adminId, CancellationToken cancellationToken);

    Task SaveLookAsync(string id, RoomLook look, Guid adminId, CancellationToken cancellationToken);

    Task SaveVariantAsync(string name, LookVariant variant, Guid adminId, CancellationToken cancellationToken);

    Task DeleteVariantAsync(string name, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Adds or changes a painter recipe: a floor or back wall that looks can name, drawn by the browser from steps.</summary>
    Task SavePainterAsync(string name, string surface, IReadOnlyList<PaintStep> steps, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Takes a painter recipe away; refused while a look paints with it.</summary>
    Task DeletePainterAsync(string name, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Adds or changes a wildlife recipe: a creature that looks can name, drawn from parts and moved in one of a few ways.</summary>
    Task SaveWildlifeAsync(string name, IReadOnlyList<PropPart> parts, WildlifeMotion motion, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Takes a wildlife recipe away; refused while a look has it.</summary>
    Task DeleteWildlifeAsync(string name, Guid adminId, CancellationToken cancellationToken);

    /// <summary>What admins did to rooms and looks, the latest first.</summary>
    Task<IReadOnlyList<AuditLine>> AuditAsync(int limit, CancellationToken cancellationToken);
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
                return new RoomSummary(group.Key, (published ?? draft ?? group.First()).Layout.Name, published?.Number, draft?.Number, draft?.GoLiveAt);
            })];
    }

    public async Task<ImportReport> ImportAsync(ContentFile file, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var applied = new List<string>();
        var unchanged = new List<string>();
        var problems = new List<string>();
        var pending = new List<string>();
        static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a, ContentFile.Json) == JsonSerializer.Serialize(b, ContentFile.Json);

        var designs = await db.PropDesigns.ToDictionaryAsync(d => d.Kind, cancellationToken);
        foreach (var design in file.PropDesignList)
        {
            if (!designs.TryGetValue(design.Kind, out var existing))
            {
                try
                {
                    db.PropDesigns.Add(PropDesign.Of(design.Kind, design.Size, design.Parts, time.GetUtcNow(), design.Growth));
                    Audit(adminId, "prop-design-saved", design.Kind, "");
                    await SaveAsync(cancellationToken);
                    applied.Add($"prop design {design.Kind}: saved");
                }
                catch (DomainException exception)
                {
                    problems.Add($"prop design {design.Kind}: {exception.Message}");
                }
            }
            else if (existing.Size == design.Size && Same(existing.Parts, design.Parts) && Same(existing.Growth, design.Growth ?? []))
            {
                unchanged.Add($"prop design {design.Kind}");
            }
            else
            {
                problems.Add($"prop design {design.Kind}: differs from the live design; change it in the admin.");
            }
        }

        // Order matters: a look names painters and creatures, and its rooms name variants, and saving a look checks the names exist, so those load first.
        var painters = await db.PainterRecipes.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var painter in file.PainterList)
        {
            if (!painters.TryGetValue(painter.Id, out var existing))
            {
                await Attempt($"painter {painter.Id}", () => SavePainterAsync(painter.Id, painter.Surface, painter.Steps, adminId, cancellationToken));
            }
            else if (existing.Surface == painter.Surface && Same(existing.Steps, painter.Steps))
            {
                unchanged.Add($"painter {painter.Id}");
            }
            else
            {
                problems.Add($"painter {painter.Id}: differs from the live recipe; change it in the admin.");
            }
        }

        // Wildlife too: a look names its creatures.
        var creatures = await db.WildlifeRecipes.AsNoTracking().ToDictionaryAsync(w => w.Id, cancellationToken);
        foreach (var creature in file.WildlifeList)
        {
            if (!creatures.TryGetValue(creature.Id, out var existing))
            {
                await Attempt($"wildlife {creature.Id}", () => SaveWildlifeAsync(creature.Id, creature.Parts, creature.Motion, adminId, cancellationToken));
            }
            else if (Same(existing.Parts, creature.Parts) && Same(existing.Motion, creature.Motion))
            {
                unchanged.Add($"wildlife {creature.Id}");
            }
            else
            {
                problems.Add($"wildlife {creature.Id}: differs from the live recipe; change it in the admin.");
            }
        }

        var looks = await db.RoomLooks.AsNoTracking().ToDictionaryAsync(l => l.Id, cancellationToken);
        var liveRooms = (await db.RoomVersions.AsNoTracking().Where(v => v.State == RoomVersionState.Published).Select(v => v.RoomId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (id, look) in file.LookList.Select(l => (l.Id, l.Look)))
        {
            if (!looks.TryGetValue(id, out var existing))
            {
                await Attempt($"look {id}", () => SaveLookAsync(id, look, adminId, cancellationToken));
            }
            else if (Same(existing.Look, look))
            {
                unchanged.Add($"look {id}");
            }
            else if (!liveRooms.Contains(id))
            {
                await Attempt($"look {id}", () => SaveLookAsync(id, look, adminId, cancellationToken), "replaced (room not published)");
            }
            else
            {
                problems.Add($"look {id}: differs from the live look; change it in the admin (it would change a live room).");
            }
        }

        var variants = await db.LookVariants.AsNoTracking().ToDictionaryAsync(v => v.Id, cancellationToken);
        foreach (var variant in file.VariantList)
        {
            if (!variants.TryGetValue(variant.Id, out var existing))
            {
                await Attempt($"variant {variant.Id}", () => SaveVariantAsync(variant.Id, new LookVariant(variant.Sky, variant.WallColour, variant.Floor), adminId, cancellationToken));
            }
            else if (existing.Sky == variant.Sky && existing.WallColour == variant.WallColour && Same(existing.Floor, variant.Floor))
            {
                unchanged.Add($"variant {variant.Id}");
            }
            else
            {
                problems.Add($"variant {variant.Id}: differs from the live variant; change it in the admin.");
            }
        }

        foreach (var room in file.RoomList)
        {
            var detail = await GetAsync(room.Id, cancellationToken);
            if (detail?.Published is { } published && Same(published, room.Layout) && detail.Draft is null)
            {
                unchanged.Add($"room {room.Id}");
            }
            else if (detail?.Draft is { } draft && Same(draft, room.Layout))
            {
                unchanged.Add($"room {room.Id} (already a draft)");
            }
            else
            {
                try
                {
                    await SaveDraftAsync(room.Id, room.Layout, adminId, null, cancellationToken);
                    applied.Add($"room {room.Id}: saved as a draft");
                }
                catch (DomainException exception)
                {
                    problems.Add($"room {room.Id}: {exception.Message}");
                }
            }
        }

        var overlays = (await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken)).Select(o => o.ToOverlay()).ToDictionary(o => (o.RoomId, o.Id));
        // After the rooms, so a draft this file just saved counts: what an overlay waits for is read from the rooms' real state.
        var states = (await db.RoomVersions.AsNoTracking().Where(v => v.State == RoomVersionState.Published || v.State == RoomVersionState.Draft).Select(v => new { v.RoomId, v.State }).ToListAsync(cancellationToken))
            .GroupBy(v => v.RoomId).ToDictionary(g => g.Key, g => g.Select(v => v.State).ToHashSet());
        foreach (var overlay in file.OverlayList)
        {
            if (!overlays.TryGetValue((overlay.RoomId, overlay.Id), out var existing))
            {
                if (!states.ContainsKey(overlay.RoomId))
                {
                    problems.Add($"overlay {overlay.RoomId}/{overlay.Id}: there is no published or draft room {overlay.RoomId}");
                    continue;
                }

                await Attempt($"overlay {overlay.RoomId}/{overlay.Id}", () => SaveOverlayAsync(overlay, adminId, cancellationToken));
            }
            else if (Same(existing, overlay))
            {
                unchanged.Add($"overlay {overlay.RoomId}/{overlay.Id}");
            }
            else
            {
                problems.Add($"overlay {overlay.RoomId}/{overlay.Id}: differs from the live overlay; change it in the admin (it would change a live room).");
            }
        }

        if (file.RoomList.Count > 0)
        {
            // The rooms of one file lead to each other, so they are only right as a set, not one at a time against the live rooms.
            problems.AddRange(await ProblemsWithAsync(file.RoomList.ToDictionary(r => r.Id, r => r.Layout), cancellationToken));
        }

        return new ImportReport(applied, unchanged, problems, pending);

        async Task Attempt(string what, Func<Task> save, string done = "saved")
        {
            try
            {
                await save();
                applied.Add($"{what}: {done}");
            }
            catch (DomainException exception)
            {
                problems.Add($"{what}: {exception.Message}");
            }
        }
    }

    public async Task<ContentExport> ExportAsync(CancellationToken cancellationToken)
    {
        var published = await db.RoomVersions.AsNoTracking().Where(v => v.State == RoomVersionState.Published).ToListAsync(cancellationToken);
        var overlays = await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken);
        return new ContentExport(
            [.. published
                .OrderBy(v => v.RoomId == RoomBook.StartingRoom ? 0 : 1).ThenBy(v => v.RoomId, StringComparer.Ordinal)
                .Select(v => new ExportedRoom(v.RoomId, v.Layout))],
            [.. overlays.Select(o => o.ToOverlay()).OrderBy(o => o.RoomId, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal)],
            new ExportedLooks(
                [.. (await db.RoomLooks.AsNoTracking().OrderBy(l => l.Id).ToListAsync(cancellationToken)).Select(l => new { l.Id, l.Look })],
                [.. (await db.LookVariants.AsNoTracking().OrderBy(v => v.Id).ToListAsync(cancellationToken)).Select(v => new { v.Id, v.Sky, v.WallColour, v.Floor })]),
            [.. (await db.PainterRecipes.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken)).Select(r => new { r.Id, r.Surface, r.Steps })],
            [.. (await db.WildlifeRecipes.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken)).Select(r => new { r.Id, r.Parts, r.Motion })],
            [.. (await db.PropDesigns.AsNoTracking().OrderBy(d => d.Kind).ToListAsync(cancellationToken)).Select(d => new { d.Kind, d.Size, d.Parts, d.Growth })]);
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
            [.. overlays.Select(o => o.ToOverlay()).OrderBy(o => o.Id, StringComparer.Ordinal)],
            versions.FirstOrDefault(v => v.State == RoomVersionState.Draft)?.Revision,
            versions.FirstOrDefault(v => v.State == RoomVersionState.Draft)?.GoLiveAt,
            // An overlay of a published room is saved by the next load; only a room that is not published yet makes its overlays wait.
            versions.Any(v => v.State == RoomVersionState.Published) ? [] : [.. ContentFile.Embedded().OverlayList.Where(o => o.RoomId == roomId && overlays.All(saved => saved.Id != o.Id)).Select(o => o.Id)]);
    }

    public async Task<IReadOnlyList<string>> SaveDraftAsync(string roomId, RoomLayout layout, Guid adminId, int? expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var versions = await db.RoomVersions.Where(v => v.RoomId == roomId).ToListAsync(cancellationToken);
        var draft = versions.FirstOrDefault(v => v.State == RoomVersionState.Draft);
        if (expectedRevision is not null && draft?.Revision != expectedRevision)
        {
            throw new DomainException($"{roomId}'s draft has been changed (or published, or dropped) since you opened it: open it again to see what is there, then make your change again.");
        }

        if (draft is null)
        {
            db.RoomVersions.Add(RoomVersion.Draft(roomId, versions.Select(v => v.Number).DefaultIfEmpty(0).Max() + 1, layout, adminId, time.GetUtcNow()));
        }
        else
        {
            draft.Edit(layout);
        }

        Audit(adminId, "draft-saved", roomId, draft is null ? "a new draft" : $"draft over revision {draft.Revision - 1}");
        await SaveAsync(cancellationToken);
        return await ProblemsWithAsync(roomId, layout, cancellationToken);
    }

    public async Task DiscardDraftAsync(string roomId, Guid adminId, CancellationToken cancellationToken)
    {
        var draft = await db.RoomVersions.FirstOrDefaultAsync(v => v.RoomId == roomId && v.State == RoomVersionState.Draft, cancellationToken)
            ?? throw new DomainException("This room has no draft.");
        db.RoomVersions.Remove(draft);
        Audit(adminId, "draft-discarded", roomId, $"version {draft.Number}");
        await SaveAsync(cancellationToken);
    }

    public async Task PublishAsync(string roomId, IReadOnlyList<string> together, Guid adminId, CancellationToken cancellationToken)
    {
        var drafts = await DraftsOfAsync(roomId, together, cancellationToken);
        await PublishAsync(drafts, adminId, "published", _ => string.Empty, cancellationToken);
    }

    public async Task ScheduleAsync(string roomId, IReadOnlyList<string> together, DateTimeOffset at, Guid adminId, CancellationToken cancellationToken)
    {
        var drafts = await DraftsOfAsync(roomId, together, cancellationToken);
        ThrowIfProblems(await ProblemsWithAsync(drafts.ToDictionary(d => d.RoomId, d => d.Layout), cancellationToken));
        var now = time.GetUtcNow();
        foreach (var draft in drafts)
        {
            draft.Schedule(at, adminId, now);
            Audit(adminId, "publish-scheduled", draft.RoomId, $"version {draft.Number} to go live at {at:u}");
        }

        await SaveAsync(cancellationToken);
    }

    public async Task UnscheduleAsync(string roomId, Guid adminId, CancellationToken cancellationToken)
    {
        var draft = await db.RoomVersions.FirstOrDefaultAsync(v => v.RoomId == roomId && v.State == RoomVersionState.Draft && v.GoLiveAt != null, cancellationToken)
            ?? throw new DomainException("This room has no scheduled draft.");
        draft.Unschedule();
        Audit(adminId, "schedule-removed", roomId, $"version {draft.Number}");
        await SaveAsync(cancellationToken);
    }

    public async Task<int> PublishDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.RoomVersions.Where(v => v.State == RoomVersionState.Draft && v.GoLiveAt != null && v.GoLiveAt <= now).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var problems = await ProblemsWithAsync(due.ToDictionary(d => d.RoomId, d => d.Layout), cancellationToken);
        if (problems.Count > 0)
        {
            foreach (var draft in due)
            {
                var by = draft.ScheduledBy;
                draft.Unschedule();
                Audit(by, "schedule-failed", draft.RoomId, Truncate($"version {draft.Number} not published: {string.Join(" ", problems)}"));
            }

            await SaveAsync(cancellationToken);
            return 0;
        }

        try
        {
            await PublishAsync(due, due[0].ScheduledBy, "published", d => $" (scheduled for {d.GoLiveAt:u})", cancellationToken);
        }
        catch (DomainException)
        {
            return 0;
        }

        return due.Count;
    }

    private async Task<List<RoomVersion>> DraftsOfAsync(string roomId, IReadOnlyList<string> together, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(together);
        string[] ids = [.. together.Append(roomId).Distinct()];
        var drafts = await db.RoomVersions.Where(v => ids.Contains(v.RoomId) && v.State == RoomVersionState.Draft).ToListAsync(cancellationToken);
        var without = ids.Where(id => drafts.All(d => d.RoomId != id)).ToList();
        if (without.Count > 0)
        {
            throw new DomainException($"{string.Join(", ", without)} has no draft.");
        }

        return drafts;
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..497] + "...";

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
        await SaveAsync(cancellationToken);
        await PublishAsync([back], adminId, "rolled-back", _ => $" (a copy of version {number})", cancellationToken);
    }

    public async Task SaveOverlayAsync(RoomOverlay overlay, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var published = await PublishedLayoutsAsync(cancellationToken);
        if (published.ContainsKey(overlay.RoomId))
        {
            var overlays = OverlaysOfRooms(published, (await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken))
                .Select(entry => entry.ToOverlay())
                .Where(o => o.RoomId != overlay.RoomId || o.Id != overlay.Id)
                .Append(overlay));
            ThrowIfProblems([.. RoomBook.Problems(published, overlays), .. await LookProblemsAsync(published, overlays, cancellationToken)]);
        }
        else
        {
            // A room that is only a draft: no player sees it yet, so the overlay is kept as it is; its props are checked against the room when the room is saved or published.
            if (!await db.RoomVersions.AsNoTracking().AnyAsync(v => v.RoomId == overlay.RoomId && v.State == RoomVersionState.Draft, cancellationToken))
            {
                throw new DomainException($"There is no published or draft room {overlay.RoomId}.");
            }

            var variants = await db.LookVariants.AsNoTracking().Select(v => v.Id).ToListAsync(cancellationToken);
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(overlay.Id) || string.IsNullOrWhiteSpace(overlay.Name))
            {
                problems.Add($"{overlay.RoomId}/{overlay.Id}: has no ID or no name");
            }

            if (overlay.Window.Problem is { } window)
            {
                problems.Add($"{overlay.RoomId}/{overlay.Id}: {window}");
            }

            if (overlay.Look is not null && !variants.Contains(overlay.Look))
            {
                problems.Add($"{overlay.RoomId}/{overlay.Id}: its look '{overlay.Look}' is not one of ({string.Join(", ", variants)}).");
            }

            ThrowIfProblems(problems);
        }

        var entry = await db.RoomOverlays.FirstOrDefaultAsync(o => o.RoomId == overlay.RoomId && o.Id == overlay.Id, cancellationToken);
        if (entry is null)
        {
            db.RoomOverlays.Add(RoomOverlayEntry.Of(overlay));
        }
        else
        {
            entry.Replace(overlay);
        }

        Audit(adminId, "overlay-saved", $"{overlay.RoomId}/{overlay.Id}", overlay.Name);
        await SaveAsync(cancellationToken);
        await library.RefreshAsync(cancellationToken);
    }

    public async Task DeleteOverlayAsync(string roomId, string overlayId, Guid adminId, CancellationToken cancellationToken)
    {
        var entry = await db.RoomOverlays.FirstOrDefaultAsync(o => o.RoomId == roomId && o.Id == overlayId, cancellationToken)
            ?? throw new DomainException($"{roomId} has no overlay {overlayId}.");
        db.RoomOverlays.Remove(entry);
        Audit(adminId, "overlay-deleted", $"{roomId}/{overlayId}", string.Empty);
        await SaveAsync(cancellationToken);
        await library.RefreshAsync(cancellationToken);
    }

    public async Task SaveLookAsync(string id, RoomLook look, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(look);
        var now = time.GetUtcNow();
        var names = await LookNamesAsync(cancellationToken);
        var entry = await db.RoomLooks.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (entry is null)
        {
            db.RoomLooks.Add(RoomLookEntry.Of(id, look, now, names));
        }
        else
        {
            entry.Replace(look, now, names);
        }

        Audit(adminId, "look-saved", id, string.Empty);
        await SaveAsync(cancellationToken);
    }

    public async Task SaveVariantAsync(string name, LookVariant variant, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var now = time.GetUtcNow();
        var names = await LookNamesAsync(cancellationToken);
        var entry = await db.LookVariants.FirstOrDefaultAsync(v => v.Id == name, cancellationToken);
        if (entry is null)
        {
            db.LookVariants.Add(LookVariantEntry.Of(name, variant, now, names));
        }
        else
        {
            entry.Replace(variant, now, names);
        }

        Audit(adminId, "variant-saved", name, string.Empty);
        await SaveAsync(cancellationToken);
    }

    public async Task DeleteVariantAsync(string name, Guid adminId, CancellationToken cancellationToken)
    {
        var entry = await db.LookVariants.FirstOrDefaultAsync(v => v.Id == name, cancellationToken)
            ?? throw new DomainException($"There is no look {name}.");
        var users = await db.RoomOverlays.AsNoTracking().Where(o => o.Look == name).Select(o => o.RoomId + "/" + o.Id).ToListAsync(cancellationToken);
        if (users.Count > 0)
        {
            throw new DomainException($"The look {name} is in use by {string.Join(", ", users)}.");
        }

        db.LookVariants.Remove(entry);
        Audit(adminId, "variant-deleted", name, string.Empty);
        await SaveAsync(cancellationToken);
    }

    public async Task SavePainterAsync(string name, string surface, IReadOnlyList<PaintStep> steps, Guid adminId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var entry = await db.PainterRecipes.FirstOrDefaultAsync(r => r.Id == name, cancellationToken);
        if (entry is null)
        {
            db.PainterRecipes.Add(PainterRecipeEntry.Of(name, surface, steps, now));
        }
        else
        {
            if (entry.Surface != surface && await PainterUsersAsync(name, cancellationToken) is { Count: > 0 } users)
            {
                throw new DomainException($"The painter {name} is in use by {string.Join(", ", users)}: it cannot change what it paints.");
            }

            entry.Replace(surface, steps, now);
        }

        Audit(adminId, "painter-saved", name, surface);
        await SaveAsync(cancellationToken);
    }

    public async Task DeletePainterAsync(string name, Guid adminId, CancellationToken cancellationToken)
    {
        var entry = await db.PainterRecipes.FirstOrDefaultAsync(r => r.Id == name, cancellationToken)
            ?? throw new DomainException($"There is no painter {name}.");
        if (await PainterUsersAsync(name, cancellationToken) is { Count: > 0 } users)
        {
            throw new DomainException($"The painter {name} is in use by {string.Join(", ", users)}.");
        }

        db.PainterRecipes.Remove(entry);
        Audit(adminId, "painter-deleted", name, string.Empty);
        await SaveAsync(cancellationToken);
    }

    public async Task SaveWildlifeAsync(string name, IReadOnlyList<PropPart> parts, WildlifeMotion motion, Guid adminId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var entry = await db.WildlifeRecipes.FirstOrDefaultAsync(r => r.Id == name, cancellationToken);
        if (entry is null)
        {
            db.WildlifeRecipes.Add(WildlifeRecipeEntry.Of(name, parts, motion, now));
        }
        else
        {
            entry.Replace(parts, motion, now);
        }

        Audit(adminId, "wildlife-saved", name, motion?.Kind ?? string.Empty);
        await SaveAsync(cancellationToken);
    }

    public async Task DeleteWildlifeAsync(string name, Guid adminId, CancellationToken cancellationToken)
    {
        var entry = await db.WildlifeRecipes.FirstOrDefaultAsync(r => r.Id == name, cancellationToken)
            ?? throw new DomainException($"There is no creature {name}.");
        if (await UsedByLooksAsync(look => look.Wildlife == name, cancellationToken) is { Count: > 0 } users)
        {
            throw new DomainException($"The creature {name} is in use by {string.Join(", ", users)}.");
        }

        db.WildlifeRecipes.Remove(entry);
        Audit(adminId, "wildlife-deleted", name, string.Empty);
        await SaveAsync(cancellationToken);
    }

    private async Task<LookNames> LookNamesAsync(CancellationToken cancellationToken) =>
        LookNames.BuiltIn.With(
            (await db.PainterRecipes.AsNoTracking().Select(r => new { r.Surface, r.Id }).ToListAsync(cancellationToken)).Select(r => (r.Surface, r.Id)),
            await db.WildlifeRecipes.AsNoTracking().Select(r => r.Id).ToListAsync(cancellationToken));

    /// <summary>The looks and the seasonal variants that paint with a painter (a variant's own floor too), by name.</summary>
    private async Task<List<string>> PainterUsersAsync(string name, CancellationToken cancellationToken)
    {
        var users = await UsedByLooksAsync(look => look.Floor?.Painter == name || look.Wall?.Painter == name, cancellationToken);
        users.AddRange(await db.LookVariants.AsNoTracking().Where(v => v.FloorPainter == name).Select(v => "variant " + v.Id).ToListAsync(cancellationToken));
        return users;
    }

    private async Task<List<string>> UsedByLooksAsync(Func<RoomLook, bool> uses, CancellationToken cancellationToken) =>
        [.. (await db.RoomLooks.AsNoTracking().ToListAsync(cancellationToken)).Where(entry => uses(entry.Look)).Select(entry => entry.Id)];

    public async Task<IReadOnlyList<AuditLine>> AuditAsync(int limit, CancellationToken cancellationToken) =>
        [.. (await db.RoomAudit.AsNoTracking().OrderByDescending(a => a.At).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken))
            .Select(a => new AuditLine(a.At, a.AdminId, a.Action, a.Target, a.Detail))];

    private void Audit(Guid? adminId, string action, string target, string detail) =>
        db.RoomAudit.Add(RoomAuditEntry.Of(adminId, action, target, detail, time.GetUtcNow()));

    private async Task PublishAsync(List<RoomVersion> drafts, Guid? adminId, string action, Func<RoomVersion, string> note, CancellationToken cancellationToken)
    {
        ThrowIfProblems(await ProblemsWithAsync(drafts.ToDictionary(d => d.RoomId, d => d.Layout), cancellationToken));
        var draftIds = drafts.Select(d => d.Id).ToList();
        var roomIds = drafts.Select(d => d.RoomId).ToList();
        try
        {
            await db.InTransactionAsync(async ct =>
            {
                var current = await db.RoomVersions.Where(v => roomIds.Contains(v.RoomId) && v.State == RoomVersionState.Published).ToListAsync(ct);
                foreach (var version in current)
                {
                    version.Archive();
                }

                if (current.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                }

                var now = time.GetUtcNow();
                foreach (var draft in await db.RoomVersions.Where(v => draftIds.Contains(v.Id)).ToListAsync(ct))
                {
                    var extra = note(draft);
                    draft.Publish(now);
                    Audit(adminId, action, draft.RoomId, $"version {draft.Number}{extra}");
                }

                await db.SaveChangesAsync(ct);
            }, cancellationToken);
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
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("Someone else changed this draft at the same moment: open it again and retry.");
        }
    }

    private async Task<Dictionary<string, RoomLayout>> PublishedLayoutsAsync(CancellationToken cancellationToken) =>
        await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Published)
            .ToDictionaryAsync(v => v.RoomId, v => v.Layout, cancellationToken);

    private Task<IReadOnlyList<string>> ProblemsWithAsync(string roomId, RoomLayout layout, CancellationToken cancellationToken) =>
        ProblemsWithAsync(new Dictionary<string, RoomLayout> { [roomId] = layout }, cancellationToken);

    private async Task<IReadOnlyList<string>> ProblemsWithAsync(IReadOnlyDictionary<string, RoomLayout> replaced, CancellationToken cancellationToken)
    {
        var layouts = await PublishedLayoutsAsync(cancellationToken);
        foreach (var (id, layout) in replaced)
        {
            layouts[id] = layout;
        }

        var overlays = OverlaysOfRooms(layouts, (await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken)).Select(entry => entry.ToOverlay()));
        return [.. RoomBook.Problems(layouts, overlays), .. await LookProblemsAsync(layouts, overlays, cancellationToken)];
    }

    /// <summary>A room is published only with a look to be drawn in, and an overlay only with a look that is there.</summary>
    private async Task<IReadOnlyList<string>> LookProblemsAsync(IReadOnlyDictionary<string, RoomLayout> layouts, IReadOnlyList<RoomOverlay> overlays, CancellationToken cancellationToken)
    {
        var looks = await db.RoomLooks.AsNoTracking().Select(l => l.Id).ToListAsync(cancellationToken);
        var variants = await db.LookVariants.AsNoTracking().Select(v => v.Id).ToListAsync(cancellationToken);
        return
        [
            .. layouts.Keys.Where(id => !looks.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{id}: has no look: add one (how it is painted and what is round it) before publishing."),
            .. layouts.Where(pair => pair.Value.Look is not null && !variants.Contains(pair.Value.Look)).Select(pair => $"{pair.Key}: its look '{pair.Value.Look}' is not one of ({string.Join(", ", variants)})."),
            .. overlays.Where(o => o.Look is not null && !variants.Contains(o.Look)).Select(o => $"{o.RoomId}/{o.Id}: its look '{o.Look}' is not one of ({string.Join(", ", variants)})."),
        ];
    }

    /// <summary>Only the overlays of rooms in the book: one of a room that is still a draft is not checked until that room is.</summary>
    private static List<RoomOverlay> OverlaysOfRooms(IReadOnlyDictionary<string, RoomLayout> layouts, IEnumerable<RoomOverlay> overlays) =>
        [.. overlays.Where(overlay => layouts.ContainsKey(overlay.RoomId))];

    private static void ThrowIfProblems(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }
    }
}
