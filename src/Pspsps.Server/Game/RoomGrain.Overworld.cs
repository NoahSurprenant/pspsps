using Orleans.Runtime;
using Pspsps.Domain;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Worlds;

namespace Pspsps.Server.Game;

// The open-world mode of the room grain (RoomKey.Overworld): every outdoor room's cats in one OverworldState, at world
// positions. Each tab is told only about the cats near its own, so what a tab knows is tracked per player (_known).
internal sealed partial class RoomGrain
{
    private OverworldState? _world;
    private IReadOnlyList<ChunkView> _chunks = [];

    // The cats each player's tab has been told about, so it is told what comes into view and what leaves it.
    private readonly Dictionary<Guid, HashSet<Guid>> _known = [];

    // The reverse: for each cat, the players whose tabs have been told about it. Kept with _known by SetKnown.
    private readonly Dictionary<Guid, HashSet<Guid>> _watching = [];

    private void ActivateOverworld()
    {
        var (worldId, _) = RoomKey.Parse(_key);
        var space = new WorldSpace(Library.All);
        _world = new OverworldState(space, WorldCatalog.Find(worldId)?.Capacity ?? 300);
        _worldMap = WorldMapView.From(Library.All);
        _chunks = ChunkViews.Of(Library.All);
        this.RegisterGrainTimer(
            static (grain, _) => grain.RemoveExpired(),
            this,
            new GrainTimerCreationOptions { DueTime = SessionLease.RenewEvery, Period = SessionLease.RenewEvery });
    }

    private async Task<RoomSnapshot> JoinOverworld(PlayerCard player, string session, string? arrival)
    {
        var world = _world!;
        var (roomId, doorId) = RoomKey.ParseArrival(arrival);
        var fur = Enum.Parse<FurColor>(player.FurColor);
        var cat = Rule(() => world.Join(
            player.PlayerId, player.DisplayName, fur, session, time.GetUtcNow(), roomId, doorId, player.Hat, player.Collar,
            player.OutfitVersion));
        if (!_counted)
        {
            _counted = true;
            metrics.RoomActivated();
        }

        return await ReseenFrom(world, player.PlayerId, roomId);
    }

    // How near counts as "already with" a friend: they are in sight and a few steps away.
    private const double GoToNear = 4;

    // The world as the cat sees it now: a tab that comes (back), or whose cat was put somewhere, is shown everything
    // afresh, so it is told nothing as news; the cats around its old and new spot are brought up to date.
    private async Task<RoomSnapshot> ReseenFrom(OverworldState world, Guid playerId, string? fallbackRoom = null)
    {
        var cat = world.Find(playerId)!;
        var visible = world.VisibleTo(playerId);
        SetKnown(playerId, [.. visible.Select(c => c.PlayerId)]);
        await SyncNear(cat);

        var here = world.Space.RoomAt(cat.Position) ?? world.Space.Rooms.First(r => fallbackRoom is null || r.Id == fallbackRoom);
        var (worldId, _) = RoomKey.Parse(_key);
        return new RoomSnapshot(
            worldId,
            here.Id,
            here.Name,
            here.Width,
            here.Depth,
            [CatView.From(cat), .. visible.Select(CatView.From)],
            [.. world.HeardBy(playerId).Select(h => ChatView.From(h.Line))],
            [.. Library.All.Where(r => r.Id != here.Id).Select(r => new RoomLink(r.Id, r.Name))],
            here.Pond is { } pond ? new PondView(pond.Center.X, pond.Center.Z, pond.Radius) : null,
            [.. here.Doors.Select(door => DoorView.From(door, Library))],
            [.. here.Props.Select(PropView.From)],
            null,
            WorldMap: _worldMap,
            Look: here.Look,
            Chunks: _chunks);
    }

    private async Task LeaveOverworld(Guid playerId, string session)
    {
        if (_world!.Leave(playerId, session))
        {
            await Departed(playerId);
        }

        GoIfEmpty();
    }

    private async Task<DoorTarget> LeaveOverworldThrough(Guid playerId, string session, string doorId)
    {
        var world = _world!;
        var cat = world.Find(playerId) ?? throw new GameRuleException("You aren't in the world.");
        var room = world.Space.RoomAt(cat.Position) ?? throw new GameRuleException("Walk up to the door to go through it.");
        var door = Rule(() => world.LeaveThrough(playerId, session, room.Id, doorId));
        await Departed(playerId);
        GoIfEmpty();
        return new DoorTarget(door.ToRoom, door.ToDoor);
    }

    private async Task MoveInOverworld(Guid playerId, double x, double z)
    {
        var world = _world!;
        var position = Rule(() => world.Move(playerId, new Position(x, z), time.GetUtcNow()));
        var cat = world.Find(playerId)!;
        await SyncNear(cat);
        await broadcaster.CatMoved(WatchersOf(playerId), playerId, position.X, position.Z);
    }

    private async Task SayInOverworld(Guid playerId, string text)
    {
        var world = _world!;
        var (line, heard) = Rule(() => world.Say(playerId, text, chatFilters.Current, time.GetUtcNow()));
        await broadcaster.ChatSaid(SessionsOf(heard), ChatView.From(line));
    }

    private async Task EmoteInOverworld(Guid playerId, string emote)
    {
        var done = Rule(() => _world!.Emote(playerId, emote, time.GetUtcNow()));
        await broadcaster.CatEmoted(WatchersOf(playerId), EmoteView.From(done));
    }

    private async Task DressInOverworld(OutfitView outfit)
    {
        var fur = Enum.Parse<FurColor>(outfit.FurColor);
        if (_world!.Dress(outfit.PlayerId, fur, outfit.Hat, outfit.Collar, outfit.Version) is { } cat)
        {
            await broadcaster.CatDressed(WatchersOf(cat.PlayerId), OutfitView.From(cat));
        }
        else
        {
            GoIfEmpty();
        }
    }

    private async Task RenameInOverworld(Guid playerId, string displayName)
    {
        if (_world!.Rename(playerId, displayName) is { } cat)
        {
            await broadcaster.CatRenamed(WatchersOf(playerId), RenameView.From(cat));
        }
        else
        {
            GoIfEmpty();
        }
    }

    private async Task SendOutOfOverworld(IReadOnlyList<Guid> playerIds, string message)
    {
        var world = _world!;
        foreach (var playerId in playerIds)
        {
            if (world.SessionOf(playerId) is { } session)
            {
                var cat = world.Find(playerId);
                if (cat is not null)
                {
                    world.Leave(playerId, session);
                    await broadcaster.SentOut(_key, session, message);
                    await Departed(playerId);
                }
            }
        }

        GoIfEmpty();
    }

    private async Task RemoveExpiredFromOverworld()
    {
        var world = _world!;
        var placed = new WorldSpace(Library.All).Rooms;
        if (placed.Count != world.Space.Rooms.Count || placed.Zip(world.Space.Rooms).Any(p => !p.First.SameAs(p.Second)))
        {
            // The map was published again or an overlay changed: start over, and the tabs walk back in to the new map.
            DeactivateOnIdle();
            return;
        }

        var expired = world.RemoveExpired(time.GetUtcNow());
        if (expired.Count > 0)
        {
            metrics.LeasesExpired("room", expired.Count);
        }

        foreach (var playerId in expired)
        {
            await Departed(playerId);
        }

        GoIfEmpty();
    }

    // The cat is out of the world already: everyone who knew it is told it is gone.
    private async Task Departed(Guid playerId)
    {
        var watchers = WatchersOf(playerId);
        var viewers = WatchingOf(playerId).ToList();
        SetKnown(playerId, []);
        _known.Remove(playerId);
        _watching.Remove(playerId);
        foreach (var viewer in viewers)
        {
            _known[viewer].Remove(playerId);
        }

        if (watchers.Count > 0)
        {
            await broadcaster.CatLeft(watchers, playerId);
        }

        // The freed place on each screen goes to the next nearest cat, if there is one.
        foreach (var viewer in viewers)
        {
            await Sync(viewer);
        }
    }

    // Brings the tabs of every cat near this one, and its own, up to date with who is in view: after it moved or joined.
    private async Task SyncNear(CatInRoom cat)
    {
        var world = _world!;
        var affected = world.CatsAround(cat.Position)
            .Where(other => other.Position.DistanceTo(cat.Position) <= WorldSpace.ViewDistance)
            .Select(other => other.PlayerId)
            .Append(cat.PlayerId)
            .Concat(WatchingOf(cat.PlayerId))
            .Distinct()
            .Where(viewer => world.ViewMayChange(viewer, cat, _known.GetValueOrDefault(viewer) ?? []))
            .ToList();
        foreach (var viewer in affected)
        {
            await Sync(viewer);
        }
    }

    private async Task Sync(Guid viewer)
    {
        var world = _world!;
        if (world.SessionOf(viewer) is not { } session)
        {
            return;
        }

        var visible = world.VisibleTo(viewer);
        var now = visible.Select(c => c.PlayerId).ToHashSet();
        var known = _known.GetValueOrDefault(viewer) ?? [];
        SetKnown(viewer, now);
        var toSession = new[] { session };
        foreach (var gone in known.Where(id => !now.Contains(id)))
        {
            await broadcaster.CatLeft(toSession, gone);
        }

        foreach (var appeared in visible.Where(c => !known.Contains(c.PlayerId)))
        {
            await broadcaster.CatJoined(toSession, CatView.From(appeared));
        }
    }

    // Records what the viewer's tab now knows, and keeps the reverse index (_watching) in step.
    private void SetKnown(Guid viewer, HashSet<Guid> now)
    {
        var before = _known.GetValueOrDefault(viewer) ?? [];
        foreach (var gone in before.Where(id => !now.Contains(id)))
        {
            _watching[gone].Remove(viewer);
        }

        foreach (var appeared in now.Where(id => !before.Contains(id)))
        {
            if (!_watching.TryGetValue(appeared, out var set))
            {
                _watching[appeared] = set = [];
            }

            set.Add(viewer);
        }

        _known[viewer] = now;
    }

    // The players whose tabs know this cat, as a copy: syncing changes the index while it is walked.
    private List<Guid> WatchingOf(Guid playerId) => _watching.TryGetValue(playerId, out var set) ? [.. set] : [];

    // The tabs to tell about a cat: its own, and every tab that has been told about it.
    private List<string> WatchersOf(Guid playerId)
    {
        var world = _world!;
        var sessions = WatchingOf(playerId)
            .Select(viewer => world.SessionOf(viewer))
            .Append(world.SessionOf(playerId))
            .OfType<string>()
            .Distinct()
            .ToList();
        return sessions;
    }

    private List<string> SessionsOf(IEnumerable<Guid> playerIds) =>
        [.. playerIds.Select(id => _world!.SessionOf(id)).OfType<string>().Distinct()];
}
