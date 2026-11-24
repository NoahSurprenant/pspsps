using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

/// <summary>
/// What the room grain keeps for the open world, without the network: who each cat was shown (<c>Known</c>), and the
/// reverse (<c>Watchers</c>: cat to the viewers shown it). <see cref="Indexed"/> false is the old way: scan every viewer's
/// set and bring every cat near the mover up to date. True is the grain's way now.
/// </summary>
internal sealed class CrowdView(OverworldState world, bool indexed)
{
    public Dictionary<Guid, HashSet<Guid>> Known { get; } = [];

    private readonly Dictionary<Guid, HashSet<Guid>> _watchers = [];

    public int Synced { get; private set; }

    public void Seen(Guid viewer) => Set(viewer, [.. world.VisibleTo(viewer).Select(c => c.PlayerId)]);

    public void Moved(CatInRoom cat)
    {
        var affected = world.CatsAround(cat.Position)
            .Where(other => other.Position.DistanceTo(cat.Position) <= WorldSpace.ViewDistance)
            .Select(other => other.PlayerId)
            .Append(cat.PlayerId)
            .Concat(WatchersOf(cat.PlayerId))
            .Distinct()
            .Where(viewer => !indexed || world.ViewMayChange(viewer, cat, Known.GetValueOrDefault(viewer) ?? []))
            .ToList();
        foreach (var viewer in affected)
        {
            Sync(viewer);
        }
    }

    public void Departed(Guid id)
    {
        var viewers = WatchersOf(id).ToList();
        Set(id, []);
        Known.Remove(id);
        _watchers.Remove(id);
        foreach (var viewer in viewers)
        {
            Known[viewer].Remove(id);
        }

        if (!indexed)
        {
            foreach (var known in Known.Values)
            {
                known.Remove(id);
            }
        }

        foreach (var viewer in viewers.Where(v => world.Find(v) is not null))
        {
            Sync(viewer);
        }
    }

    private IEnumerable<Guid> WatchersOf(Guid id) =>
        indexed
            ? _watchers.GetValueOrDefault(id)?.ToList() ?? []
            : Known.Where(entry => entry.Value.Contains(id)).Select(entry => entry.Key).ToList();

    private void Sync(Guid viewer)
    {
        Synced++;
        Seen(viewer);
    }

    private void Set(Guid viewer, HashSet<Guid> now)
    {
        var before = Known.GetValueOrDefault(viewer) ?? [];
        foreach (var gone in before.Where(id => !now.Contains(id)))
        {
            _watchers[gone].Remove(viewer);
        }

        foreach (var appeared in now.Where(id => !before.Contains(id)))
        {
            if (!_watchers.TryGetValue(appeared, out var set))
            {
                _watchers[appeared] = set = [];
            }

            set.Add(viewer);
        }

        Known[viewer] = now;
    }
}
