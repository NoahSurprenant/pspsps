namespace Pspsps.Domain.Rooms;

/// <summary>
/// What each player's tab has been shown of the open world (<see cref="OverworldState.VisibleTo"/> as of its last sync),
/// and the reverse: for each cat, the players shown it. After a cat moves, <see cref="Affected"/> names the viewers whose
/// screen can differ, so only they are re-sorted. The farthest cat on each full screen is kept with the set, so a viewer's
/// check is a distance compare; both change only through <see cref="Set"/> and <see cref="Forget"/>, which keeps them valid.
/// A viewer that was never synced, or whose sync failed (<see cref="MarkStale"/>), is always named until its next
/// <see cref="Set"/>, so a missed update heals instead of lingering.
/// </summary>
public sealed class ViewLedger
{
    private static readonly HashSet<Guid> None = [];

    private readonly Dictionary<Guid, Shown> _shown = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _watching = [];
    private readonly HashSet<Guid> _stale = [];

    /// <summary>The cats the viewer was shown at its last <see cref="Set"/>; a set that is replaced, never changed, by a later one (except by <see cref="Forget"/>).</summary>
    public IReadOnlySet<Guid> KnownBy(Guid viewer) => _shown.TryGetValue(viewer, out var shown) ? shown.Cats : None;

    /// <summary>The players shown this cat, as a copy.</summary>
    public List<Guid> Watching(Guid cat) => _watching.TryGetValue(cat, out var viewers) ? [.. viewers] : [];

    /// <summary>The viewer, standing at <paramref name="at"/>, has now been shown <paramref name="visible"/> (nearest first).</summary>
    public void Set(Guid viewer, Position at, IReadOnlyList<CatInRoom> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var cats = visible.Select(c => c.PlayerId).ToHashSet();
        if (_shown.TryGetValue(viewer, out var before))
        {
            foreach (var gone in before.Cats.Where(id => !cats.Contains(id)))
            {
                Unwatch(gone, viewer);
            }
        }

        foreach (var appeared in cats.Where(id => before is null || !before.Cats.Contains(id)))
        {
            if (!_watching.TryGetValue(appeared, out var viewers))
            {
                _watching[appeared] = viewers = [];
            }

            viewers.Add(viewer);
        }

        var last = visible.Count > 0 ? visible[^1] : null;
        _shown[viewer] = new Shown(cats, last?.Position.DistanceTo(at) ?? 0, last?.PlayerId ?? Guid.Empty, visible.Count >= WorldSpace.MaxVisibleCats);
        _stale.Remove(viewer);
    }

    /// <summary>The viewer's screen is not known to match what it was shown: it is named by <see cref="Affected"/> until its next <see cref="Set"/>.</summary>
    public void MarkStale(Guid viewer) => _stale.Add(viewer);

    /// <summary>
    /// The cat has left the world: forgets what it was shown and where it showed. Returns the players that were shown it,
    /// each marked stale until it is set again (a place on its screen is free).
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
    /// and any stale one. Every other viewer would be shown exactly what it was.
    /// </summary>
    public List<Guid> Affected(OverworldState world, CatInRoom mover)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mover);
        return [.. world.CatsAround(mover.Position)
            .Where(other => other.Position.DistanceTo(mover.Position) <= WorldSpace.ViewDistance)
            .Select(other => other.PlayerId)
            .Append(mover.PlayerId)
            .Concat(Watching(mover.PlayerId))
            .Concat(_stale)
            .Distinct()
            .Where(viewer => MayChange(world, viewer, mover))];
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

    private sealed record Shown(HashSet<Guid> Cats, double FarthestDistance, Guid FarthestId, bool Full);
}
