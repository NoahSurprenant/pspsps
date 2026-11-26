using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Worlds;

namespace Pspsps.Application.Rooms;

public sealed class RoomLibrary(IServiceScopeFactory scopes, TimeProvider time) : IRoomLibrary
{
    private volatile RoomBook? _book;
    private volatile RoomBook? _preview;
    private PreviewRooms? _previewRooms;

    private RoomBook Book => _book ?? throw new InvalidOperationException("The rooms haven't been loaded yet.");

    public RoomDefinition? Find(string id) => Book.Find(id, time.GetUtcNow());

    public IReadOnlyList<RoomDefinition> All => Book.At(time.GetUtcNow());

    public RoomDefinition Start => All[0];

    public IRoomLibrary OfWorld(string worldId) => worldId == WorldCatalog.PreviewId ? _previewRooms ??= new PreviewRooms(() => _preview ?? Book, time) : this;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var layouts = await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Published)
            .ToDictionaryAsync(v => v.RoomId, v => v.Layout, cancellationToken);
        var entries = await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken);
        IReadOnlyList<RoomOverlay> overlays = [.. entries.Where(entry => layouts.ContainsKey(entry.RoomId)).Select(entry => entry.ToOverlay())];
        // An overlay saved for a room's draft waits out of the live rooms until that draft is published; the preview has it from the start.
        _book = RoomBook.Create(layouts, RoomBook.OverlaysThatFit(layouts, overlays));

        var drafts = (await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Draft)
            .ToListAsync(cancellationToken))
            .GroupBy(v => v.RoomId)
            .ToDictionary(group => group.Key, group => group.MaxBy(v => v.Revision)!.Layout);
        var played = new Dictionary<string, RoomLayout>(layouts);
        foreach (var (roomId, layout) in drafts)
        {
            played[roomId] = layout;
        }

        // The preview plays each draft with the overlays that go live with it.
        var waiting = (await db.RoomVersions.AsNoTracking().Where(v => v.State == RoomVersionState.Draft && v.Overlays != null).ToListAsync(cancellationToken))
            .GroupBy(v => v.RoomId).Select(group => group.MaxBy(v => v.Revision)!).SelectMany(v => v.Overlays!).ToList();
        var previewOverlays = (IReadOnlyList<RoomOverlay>)[.. overlays.Where(o => !waiting.Any(w => w.RoomId == o.RoomId && w.Id == o.Id)), .. waiting.Where(w => played.ContainsKey(w.RoomId))];

        // Drafts that don't fit together yet (an admin part way through an edit) leave the preview playing the published rooms; they are never touched.
        _preview = RoomBook.Problems(played, previewOverlays).Count == 0 ? RoomBook.Create(played, previewOverlays) : null;
    }

    private sealed class PreviewRooms(Func<RoomBook> book, TimeProvider time) : IRoomLibrary
    {
        public RoomDefinition? Find(string id) => book().Find(id, time.GetUtcNow());

        public IReadOnlyList<RoomDefinition> All => book().At(time.GetUtcNow());

        public RoomDefinition Start => All[0];

        public IRoomLibrary OfWorld(string worldId) => this;

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>A library of a fixed set of rooms, for tests that need no database.</summary>
public sealed class FixedRoomLibrary(RoomBook book, TimeProvider? time = null, RoomBook? preview = null) : IRoomLibrary
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The starting rooms (<see cref="RoomCatalog"/>), with no overlays.</summary>
    public static FixedRoomLibrary Starting(TimeProvider? time = null) =>
        new(RoomBook.Create(RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of), []), time);

    public RoomDefinition? Find(string id) => book.Find(id, _time.GetUtcNow());

    public IReadOnlyList<RoomDefinition> All => book.At(_time.GetUtcNow());

    public RoomDefinition Start => All[0];

    public IRoomLibrary OfWorld(string worldId) => worldId == WorldCatalog.PreviewId && preview is not null ? new FixedRoomLibrary(preview, _time) : this;

    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
