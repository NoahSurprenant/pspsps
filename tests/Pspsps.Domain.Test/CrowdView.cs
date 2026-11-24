using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

/// <summary>
/// The room grain's syncing of the open world without the network, over the same <see cref="ViewLedger"/> the grain uses.
/// <c>filtered</c> false brings every viewer near the mover (and who was shown it) up to date, the way it was done before
/// <see cref="ViewLedger.Affected"/>; true syncs only the viewers it names.
/// </summary>
internal sealed class CrowdView(OverworldState world, bool filtered)
{
    public ViewLedger Views { get; } = new();

    public int Synced { get; private set; }

    public void Seen(Guid viewer) => Sync(viewer);

    public void Moved(CatInRoom cat)
    {
        var affected = filtered
            ? Views.Affected(world, cat)
            : [.. world.CatsAround(cat.Position)
                .Where(other => other.Position.DistanceTo(cat.Position) <= WorldSpace.ViewDistance)
                .Select(other => other.PlayerId)
                .Append(cat.PlayerId)
                .Concat(Views.Watching(cat.PlayerId))
                .Distinct()];
        foreach (var viewer in affected)
        {
            Sync(viewer);
        }
    }

    public void Departed(Guid id)
    {
        foreach (var viewer in Views.Forget(id).Where(v => world.Find(v) is not null))
        {
            Sync(viewer);
        }
    }

    private void Sync(Guid viewer)
    {
        Synced++;
        Views.Set(viewer, world.Find(viewer)!.Position, world.VisibleTo(viewer));
    }
}
