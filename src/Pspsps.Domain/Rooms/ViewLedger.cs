namespace Pspsps.Domain.Rooms;

/// <summary>
/// What each player's tab has been shown of the open world, and the reverse: for each cat, the players shown it. After a
/// cat moves, <see cref="Affected"/> names the viewers whose screen can differ, so only they are re-sorted. The farthest cat
/// on each full screen is kept with the set, so a viewer's check is a distance compare; the sets change only through this
/// type, and a viewer's screen is trusted only after <see cref="Settle"/> or <see cref="Set"/>: <see cref="Affected"/> marks
/// each viewer it names stale, and a stale viewer is named on every later move (while its cat is in the world) until it is
/// settled, so a sync that failed heals. A viewer never recorded that is out of range of a move is not named, because the
/// move cannot show it anything.
/// </summary>
public sealed class ViewLedger
{
    private readonly Dictionary<Guid, Shown> _shown = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _watching = [];
    private readonly HashSet<Guid> _stale = [];

    /// <summary>The cats the viewer has been shown, as a copy.</summary>
    public HashSet<Guid> KnownBy(Guid viewer) => _shown.TryGetValue(viewer, out var shown) ? [.. shown.Cats] : [];

    /// <summary>The players shown this cat, as a copy.</summary>
    public List<Guid> Watching(Guid cat) => _watching.TryGetValue(cat, out var viewers) ? [.. viewers] : [];

    /// <summary>The viewer, standing at <paramref name="at"/>, has now been shown exactly <paramref name="visible"/> (nearest first), told or not.</summary>
    public void Set(Guid viewer, Position at, IReadOnlyList<CatInRoom> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var cats = visible.Select(c => c.PlayerId).ToHashSet();
        var shown = ShownOf(viewer);
        foreach (var gone in shown.Cats.Where(id => !cats.Contains(id)).ToList())
        {
            Untold(viewer, gone);
        }

        foreach (var appeared in cats.Where(id => !shown.Cats.Contains(id)))
        {
            Told(viewer, appeared);
        }

        Settle(viewer, at, visible);
    }

    /// <summary>What a sync of the viewer would tell it: the cats to take off its screen, and those to put on.</summary>
    public (List<Guid> Gone, List<CatInRoom> Appeared) Changes(Guid viewer, IReadOnlyList<CatInRoom> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var cats = visible.Select(c => c.PlayerId).ToHashSet();
        var known = _shown.TryGetValue(viewer, out var shown) ? shown.Cats : [];
        return ([.. known.Where(id => !cats.Contains(id))], [.. visible.Where(c => !known.Contains(c.PlayerId))]);
    }

    /// <summary>The viewer's tab has been told this cat is in view: recorded as each tell succeeds, so a failure part-way leaves what was told exact.</summary>
    public void Told(Guid viewer, Guid cat)
    {
        if (ShownOf(viewer).Cats.Add(cat))
        {
            if (!_watching.TryGetValue(cat, out var viewers))
            {
                _watching[cat] = viewers = [];
            }

            viewers.Add(viewer);
        }
    }

    /// <summary>The viewer's tab has been told this cat is out of view.</summary>
    public void Untold(Guid viewer, Guid cat)
    {
        if (_shown.TryGetValue(viewer, out var shown))
        {
            shown.Cats.Remove(cat);
        }

        Unwatch(cat, viewer);
    }

    /// <summary>
    /// The viewer, standing at <paramref name="at"/>, has been told all of <paramref name="visible"/> (through <see cref="Told"/>
    /// and <see cref="Untold"/>): its screen is trusted again, and the farthest cat on it is noted.
    /// </summary>
    public void Settle(Guid viewer, Position at, IReadOnlyList<CatInRoom> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var shown = ShownOf(viewer);
        var last = visible.Count > 0 ? visible[^1] : null;
        shown.FarthestDistance = last?.Position.DistanceTo(at) ?? 0;
        shown.FarthestId = last?.PlayerId ?? Guid.Empty;
        shown.Full = visible.Count >= WorldSpace.MaxVisibleCats;
        _stale.Remove(viewer);
    }

    /// <summary>The viewer's screen is not known to match what it should show: it is named by <see cref="Affected"/> until settled.</summary>
    public void MarkStale(Guid viewer) => _stale.Add(viewer);

    /// <summary>
    /// The cat has left the world: forgets what it was shown and where it showed. Returns the players that were shown it,
    /// each marked stale until settled (a place on its screen is free).
    /// </summary>
    public List<Guid> Forget(Guid cat)
    {
        _stale.Remove(cat);
        var viewers = Watching(cat);
        _watching.Remove(cat);
        if (_shown.Remove(cat, out var own))
        {
            foreach (var id in own.Cats)
            {
                Unwatch(id, cat);
            }
        }

        foreach (var viewer in viewers)
        {
            if (_shown.TryGetValue(viewer, out var shown))
            {
                shown.Cats.Remove(cat);
            }

            _stale.Add(viewer);
        }

        return viewers;
    }

    /// <summary>
    /// The viewers whose screen can differ now that <paramref name="mover"/> has moved or come in: itself, those shown it, those
    /// it now ranks among the nearest <see cref="WorldSpace.MaxVisibleCats"/> within <see cref="WorldSpace.ViewDistance"/> of,
    /// those in range that were never recorded, and any stale one still in the world. Each is marked stale until settled.
    /// Every other viewer would be shown exactly what it was.
    /// </summary>
    public List<Guid> Affected(OverworldState world, CatInRoom mover)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mover);
        foreach (var gone in _stale.Where(id => world.Find(id) is null).ToList())
        {
            _stale.Remove(gone);
        }

        var affected = world.CatsAround(mover.Position)
            .Where(other => other.Position.DistanceTo(mover.Position) <= WorldSpace.ViewDistance)
            .Select(other => other.PlayerId)
            .Append(mover.PlayerId)
            .Concat(Watching(mover.PlayerId))
            .Concat(_stale)
            .Distinct()
            .Where(viewer => MayChange(world, viewer, mover))
            .ToList();
        _stale.UnionWith(affected);
        return affected;
    }

    private bool MayChange(OverworldState world, Guid viewerId, CatInRoom mover)
    {
        if (viewerId == mover.PlayerId || _stale.Contains(viewerId) || !_shown.TryGetValue(viewerId, out var shown) || shown.Cats.Contains(mover.PlayerId))
        {
            return true;
        }

        if (world.Find(viewerId) is not { } viewer)
        {
            return false;
        }

        var distance = mover.Position.DistanceTo(viewer.Position);
        if (distance > WorldSpace.ViewDistance)
        {
            return false;
        }

        // A full screen: the mover gets in only by beating the farthest cat on it (nearest first, ties by ID).
        return !shown.Full || Compare(distance, mover.PlayerId, shown.FarthestDistance, shown.FarthestId) < 0;
    }

    private Shown ShownOf(Guid viewer)
    {
        if (!_shown.TryGetValue(viewer, out var shown))
        {
            _shown[viewer] = shown = new Shown();
        }

        return shown;
    }

    private void Unwatch(Guid cat, Guid viewer)
    {
        if (_watching.TryGetValue(cat, out var viewers) && viewers.Remove(viewer) && viewers.Count == 0)
        {
            _watching.Remove(cat);
        }
    }

    private static int Compare(double distance, Guid id, double otherDistance, Guid otherId)
    {
        var byDistance = distance.CompareTo(otherDistance);
        return byDistance != 0 ? byDistance : id.CompareTo(otherId);
    }

    private sealed class Shown
    {
        public HashSet<Guid> Cats { get; } = [];

        public double FarthestDistance { get; set; }

        public Guid FarthestId { get; set; }

        public bool Full { get; set; }
    }
}
