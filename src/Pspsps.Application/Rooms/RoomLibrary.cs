using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

public sealed class RoomLibrary(IServiceScopeFactory scopes, TimeProvider time) : IRoomLibrary
{
    private volatile RoomBook? _book;

    private RoomBook Book => _book ?? throw new InvalidOperationException("The rooms haven't been loaded yet.");

    public RoomDefinition? Find(string id) => Book.Find(id, time.GetUtcNow());

    public IReadOnlyList<RoomDefinition> All => Book.At(time.GetUtcNow());

    public RoomDefinition Start => All[0];

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var layouts = await db.RoomVersions.AsNoTracking()
            .Where(v => v.State == RoomVersionState.Published)
            .ToDictionaryAsync(v => v.RoomId, v => v.Layout, cancellationToken);
        var entries = await db.RoomOverlays.AsNoTracking().ToListAsync(cancellationToken);
        _book = RoomBook.Create(layouts, [.. entries.Select(entry => entry.ToOverlay())]);
    }
}

/// <summary>A library of a fixed set of rooms, for tests that need no database.</summary>
public sealed class FixedRoomLibrary(RoomBook book, TimeProvider? time = null) : IRoomLibrary
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The starting rooms (<see cref="RoomCatalog"/>), with no overlays.</summary>
    public static FixedRoomLibrary Starting(TimeProvider? time = null) =>
        new(RoomBook.Create(RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of), []), time);

    public RoomDefinition? Find(string id) => book.Find(id, _time.GetUtcNow());

    public IReadOnlyList<RoomDefinition> All => book.At(_time.GetUtcNow());

    public RoomDefinition Start => All[0];

    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
