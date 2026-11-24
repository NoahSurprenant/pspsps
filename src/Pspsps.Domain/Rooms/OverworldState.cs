using Pspsps.Domain.Fishing;
using Pspsps.Domain.Players;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// Who is out in the open world and what each of them has heard. Every outdoor room's cats are in this one state, at
/// world positions, so a cat walks from one room into the next with nothing to hand off. A player sees the nearest
/// <see cref="WorldSpace.MaxVisibleCats"/> cats within <see cref="WorldSpace.ViewDistance"/>, and hears what is said
/// within that distance of them. Lives in memory only, like <see cref="RoomState"/>.
/// </summary>
public sealed class OverworldState(WorldSpace space, int capacity)
{
    /// <summary>How many lines each player keeps of what they heard: what a report keeps of what was said to them.</summary>
    public const int HeardHistory = 20;

    private readonly Dictionary<Guid, CatInRoom> _cats = [];
    private readonly Dictionary<(int X, int Z), HashSet<Guid>> _cells = [];
    private readonly Dictionary<Guid, SessionLease> _leases = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastEmotes = [];
    private readonly Dictionary<Guid, Queue<HeardLine>> _heard = [];
    private readonly Dictionary<Guid, FishingLine> _lines = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastCasts = [];
    private readonly Dictionary<Guid, (double Metres, DateTimeOffset At)> _walks = [];

    public WorldSpace Space { get; } = space;

    /// <summary>Cats out in the open at once, over every outdoor room.</summary>
    public int Capacity { get; } = capacity;

    /// <summary>The fastest a cat may cross the world, in metres a second: twice the browser's walking pace.</summary>
    public const double MaxWalkSpeed = 8;

    /// <summary>How far a cat may go at once before its walking pace limits it: a tap across the screen, and some.</summary>
    public const double WalkBurst = 60;

    /// <summary>The cats here, at world positions.</summary>
    public IReadOnlyCollection<CatInRoom> Cats => _cats.Values;

    public IReadOnlyList<string> Sessions => [.. _leases.Values.Select(lease => lease.Session)];

    /// <summary>
    /// Puts a cat in the world, at the spawn point of the room <paramref name="roomId"/>, or just inside its door
    /// <paramref name="throughDoor"/>, controlled by <paramref name="session"/>. A cat already here (the player's other
    /// tab) keeps its spot and passes to the new session.
    /// </summary>
    public CatInRoom Join(
        Guid playerId,
        string displayName,
        FurColor furColor,
        string session,
        DateTimeOffset now,
        string roomId,
        string? throughDoor = null,
        string? hat = null,
        string? collar = null,
        int outfitVersion = 0)
    {
        if (_cats.TryGetValue(playerId, out var existing))
        {
            if (!Controls(playerId, session))
            {
                _lines.Remove(playerId);
            }

            _leases[playerId] = SessionLease.Start(session, now);
            return Dress(playerId, furColor, hat, collar, outfitVersion) ?? existing;
        }

        if (_cats.Count >= Capacity)
        {
            throw new DomainException("The world is full.");
        }

        var room = Space.Rooms.FirstOrDefault(r => r.Id == roomId) ?? throw new DomainException($"{roomId} isn't on the world map.");
        var local = throughDoor is null ? room.Spawn : room.GetDoor(throughDoor).Arrival;
        var cat = new CatInRoom(
            playerId, displayName, furColor, Space.ToWorld(roomId, local), Hat: hat, Collar: collar, OutfitVersion: outfitVersion);
        Put(cat);
        _leases[playerId] = SessionLease.Start(session, now);
        _walks[playerId] = (WalkBurst, now);
        return cat;
    }

    /// <summary>Removes the cat when <paramref name="session"/> controls it; false when it wasn't here or another tab has it.</summary>
    public bool Leave(Guid playerId, string session)
    {
        if (!Controls(playerId, session))
        {
            return false;
        }

        Forget(playerId);
        return true;
    }

    /// <summary>
    /// Takes the cat out through the door <paramref name="doorId"/> of the room <paramref name="roomId"/>, when
    /// <paramref name="session"/> controls it and it stands on that door; returns the door. Refused, and the cat stays,
    /// when it isn't on the door.
    /// </summary>
    public Door LeaveThrough(Guid playerId, string session, string roomId, string doorId)
    {
        var cat = Get(playerId);
        if (!Controls(playerId, session))
        {
            throw new DomainException("You aren't in this room.");
        }

        var room = Space.RoomAt(cat.Position);
        if (room is null || room.Id != roomId)
        {
            throw new DomainException("Walk up to the door to go through it.");
        }

        var door = room.GetDoor(doorId);
        if (!door.InReach(Space.ToLocal(roomId, cat.Position)))
        {
            throw new DomainException("Walk up to the door to go through it.");
        }

        Forget(playerId);
        return door;
    }

    public CatRenewal Renew(Guid playerId, string session, DateTimeOffset now)
    {
        if (Controls(playerId, session))
        {
            _leases[playerId] = _leases[playerId].Renew(now);
            return CatRenewal.Renewed;
        }

        return _cats.ContainsKey(playerId) ? CatRenewal.TakenOver : CatRenewal.Gone;
    }

    public IReadOnlyList<Guid> RemoveExpired(DateTimeOffset now)
    {
        var expired = _leases.Where(l => l.Value.HasExpired(now)).Select(l => l.Key).ToList();
        foreach (var playerId in expired)
        {
            Forget(playerId);
        }

        return expired;
    }

    /// <summary>
    /// Walks the cat towards <paramref name="target"/>, a world spot, at <paramref name="now"/>, stopping at the edge of the
    /// world's floor and where the cat's pace runs out: it may cross <see cref="MaxWalkSpeed"/> metres a second, with
    /// <see cref="WalkBurst"/> metres in hand. A cat that walks off reels its line in, with nothing on it.
    /// </summary>
    public Position Move(Guid playerId, Position target, DateTimeOffset now)
    {
        var cat = Get(playerId);
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Z))
        {
            throw new DomainException("That isn't a spot in the world.");
        }

        var (metres, at) = _walks.GetValueOrDefault(playerId, (WalkBurst, now));
        metres = Math.Min(WalkBurst, metres + (Math.Max(0, (now - at).TotalSeconds) * MaxWalkSpeed));
        var position = Space.Clamp(target);
        var distance = position.DistanceTo(cat.Position);
        if (distance > metres)
        {
            var fraction = metres / distance;
            position = Space.Clamp(new Position(
                cat.Position.X + ((position.X - cat.Position.X) * fraction), cat.Position.Z + ((position.Z - cat.Position.Z) * fraction)));
            distance = metres;
        }

        _walks[playerId] = (metres - distance, now);
        Put(cat with { Position = position, Pose = CatPose.Standing });
        _lines.Remove(playerId);
        return position;
    }

    public CatInRoom? Dress(Guid playerId, FurColor furColor, string? hat, string? collar, int version)
    {
        if (!_cats.TryGetValue(playerId, out var cat) || version < cat.OutfitVersion)
        {
            return null;
        }

        var dressed = cat with { FurColor = furColor, Hat = hat, Collar = collar, OutfitVersion = version };
        _cats[playerId] = dressed;
        return dressed;
    }

    public CatInRoom? Rename(Guid playerId, string displayName)
    {
        if (!_cats.TryGetValue(playerId, out var cat))
        {
            return null;
        }

        var renamed = cat with { DisplayName = displayName };
        _cats[playerId] = renamed;
        return renamed;
    }

    public CatEmote Emote(Guid playerId, string emote, DateTimeOffset now)
    {
        var cat = Get(playerId);
        if (!Emotes.IsKnown(emote))
        {
            throw new DomainException("That isn't an emote.");
        }

        if (_lastEmotes.TryGetValue(playerId, out var last) && now - last < RoomState.EmoteCooldown)
        {
            throw new DomainException("Not so fast: one emote a second.");
        }

        _lastEmotes[playerId] = now;
        if (Emotes.PoseOf(emote) is { } pose)
        {
            _cats[playerId] = cat with { Pose = pose };
        }

        return new CatEmote(playerId, emote, now);
    }

    /// <summary>
    /// Says a line, through <paramref name="filter"/>, where the cat stands. Returns the line and who heard it: every cat
    /// that is shown the speaker (within <see cref="WorldSpace.ViewDistance"/>, among its nearest <see cref="WorldSpace.MaxVisibleCats"/>), the speaker too. Each listener keeps it in what they
    /// heard (<see cref="HeardBy"/>).
    /// </summary>
    public (ChatLine Line, IReadOnlyList<Guid> Heard) Say(Guid playerId, string text, ChatFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var speaker = Get(playerId);
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("Say something.");
        }

        if (trimmed.Length > RoomState.MaxChatLength)
        {
            throw new DomainException($"Chat messages are at most {RoomState.MaxChatLength} characters.");
        }

        var line = new ChatLine(playerId, filter.Apply(trimmed), now);
        var heard = CatsAround(speaker.Position)
            .Where(cat => cat.PlayerId == playerId || VisibleTo(cat.PlayerId).Any(seen => seen.PlayerId == playerId))
            .Select(cat => cat.PlayerId)
            .ToList();
        foreach (var listener in heard)
        {
            if (!_heard.TryGetValue(listener, out var queue))
            {
                _heard[listener] = queue = new Queue<HeardLine>();
            }

            queue.Enqueue(new HeardLine(line, speaker.Position));
            while (queue.Count > HeardHistory)
            {
                queue.Dequeue();
            }
        }

        return (line, heard);
    }

    /// <summary>What the player heard lately, oldest first: what a report against someone who spoke to them keeps.</summary>
    public IReadOnlyList<HeardLine> HeardBy(Guid playerId) =>
        _heard.TryGetValue(playerId, out var queue) ? [.. queue] : [];

    /// <summary>
    /// The cats this player is shown: the nearest <see cref="WorldSpace.MaxVisibleCats"/> other cats within
    /// <see cref="WorldSpace.ViewDistance"/>, nearest first (ties by ID).
    /// </summary>
    public IReadOnlyList<CatInRoom> VisibleTo(Guid playerId)
    {
        var me = Get(playerId);
        return [.. CatsAround(me.Position)
            .Where(cat => cat.PlayerId != playerId)
            .Select(cat => (Cat: cat, Distance: cat.Position.DistanceTo(me.Position)))
            .Where(x => x.Distance <= WorldSpace.ViewDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Cat.PlayerId)
            .Take(WorldSpace.MaxVisibleCats)
            .Select(x => x.Cat)];
    }

    /// <summary>
    /// Whether what <paramref name="viewerId"/> is shown can differ now that <paramref name="mover"/> has moved (or come in),
    /// given <paramref name="known"/>, the cats it was shown before: when it is the mover, was shown the mover, or the mover
    /// now ranks among its nearest <see cref="WorldSpace.MaxVisibleCats"/> within <see cref="WorldSpace.ViewDistance"/>.
    /// False means <see cref="VisibleTo"/> would give the same cats as <paramref name="known"/>, so there is nothing to sort.
    /// Valid while <paramref name="known"/> was <see cref="VisibleTo"/> before the move and no other cat has moved since.
    /// </summary>
    public bool ViewMayChange(Guid viewerId, CatInRoom mover, IReadOnlySet<Guid> known)
    {
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(known);
        if (viewerId == mover.PlayerId || known.Contains(mover.PlayerId))
        {
            return true;
        }

        if (!_cats.TryGetValue(viewerId, out var viewer))
        {
            return false;
        }

        var distance = mover.Position.DistanceTo(viewer.Position);
        if (distance > WorldSpace.ViewDistance)
        {
            return false;
        }

        if (known.Count < WorldSpace.MaxVisibleCats)
        {
            return true;
        }

        // A full screen: the mover gets in only by beating the farthest cat on it (nearest first, ties by ID).
        var farthest = (Distance: double.NegativeInfinity, Id: Guid.Empty);
        foreach (var id in known)
        {
            if (!_cats.TryGetValue(id, out var cat))
            {
                return true;
            }

            var d = cat.Position.DistanceTo(viewer.Position);
            if (Compare(d, id, farthest.Distance, farthest.Id) > 0)
            {
                farthest = (d, id);
            }
        }

        return Compare(distance, mover.PlayerId, farthest.Distance, farthest.Id) < 0;
    }

    // The order a screen lists cats in: nearest first, ties by ID.
    private static int Compare(double distance, Guid id, double otherDistance, Guid otherId)
    {
        var byDistance = distance.CompareTo(otherDistance);
        return byDistance != 0 ? byDistance : id.CompareTo(otherId);
    }

    /// <summary>The cat's line in the water, if any: a bite applies only to the line it was cast for.</summary>
    public FishingLine? LineOf(Guid playerId) => _lines.GetValueOrDefault(playerId);

    /// <summary>
    /// Casts the cat's line into a pond: it must stand at the water's edge, in whichever room the pond is, with no line
    /// already out. <paramref name="roll"/> in [0, 1) picks when the fish bites.
    /// </summary>
    public FishingLine Cast(Guid playerId, DateTimeOffset now, double roll)
    {
        var cat = Get(playerId);
        if (!Space.Rooms.Any(room => room.Pond is not null))
        {
            throw new DomainException("There's no water to fish in here.");
        }

        if (!Space.Rooms.Any(room => room.Pond is { } pond && pond.InReach(Space.ToLocal(room.Id, cat.Position))))
        {
            throw new DomainException("Walk up to the pond to fish.");
        }

        if (_lines.ContainsKey(playerId))
        {
            throw new DomainException("Your line is already in the water.");
        }

        if (_lastCasts.TryGetValue(playerId, out var last) && now - last < RoomState.CastCooldown)
        {
            throw new DomainException("Not so fast: wait a moment before casting again.");
        }

        var line = FishingLine.Cast(now, roll);
        _lines[playerId] = line;
        _lastCasts[playerId] = now;
        return line;
    }

    /// <summary>The fish bites <paramref name="line"/>, if it is still the cat's line: returns the session to tell, or null.</summary>
    public string? Bite(Guid playerId, FishingLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!ReferenceEquals(LineOf(playerId), line) || !_leases.TryGetValue(playerId, out var lease))
        {
            return null;
        }

        line.Bite(now);
        return lease.Session;
    }

    /// <summary>A bite not reeled in gets away once its window is over: the line comes out empty. Returns the session to tell, or null.</summary>
    public string? LetGetAway(Guid playerId, FishingLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!ReferenceEquals(LineOf(playerId), line) || !line.HasGotAway(now) || !_leases.TryGetValue(playerId, out var lease))
        {
            return null;
        }

        _lines.Remove(playerId);
        return lease.Session;
    }

    /// <summary>Reels the cat's line in: a fish within the bite window, nothing if too early or too late. The line is out of the water after.</summary>
    public ReelResult Reel(Guid playerId, DateTimeOffset now, double roll)
    {
        Get(playerId);
        if (!_lines.Remove(playerId, out var line))
        {
            throw new DomainException("Cast your line first.");
        }

        _lastCasts[playerId] = now;
        return line.Reel(now, roll);
    }

    /// <summary>
    /// Puts the cat at the cat <paramref name="friendId"/>, a step to its side, and returns where. Null when it already
    /// stands within <paramref name="near"/> metres of it.
    /// </summary>
    public Position? GoTo(Guid playerId, Guid friendId, double near)
    {
        var cat = Get(playerId);
        var friend = _cats.TryGetValue(friendId, out var found) ? found : throw new DomainException("They aren't in the world.");
        if (cat.Position.DistanceTo(friend.Position) <= near)
        {
            return null;
        }

        var position = Space.Clamp(new Position(friend.Position.X + 1.5, friend.Position.Z));
        Put(cat with { Position = position, Pose = CatPose.Standing });
        _lines.Remove(playerId);
        return position;
    }

    public bool Has(Guid playerId, string session) => _cats.ContainsKey(playerId) && Controls(playerId, session);

    /// <summary>The cat, or null when it isn't here.</summary>
    public CatInRoom? Find(Guid playerId) => _cats.GetValueOrDefault(playerId);

    /// <summary>The session controlling the cat, to tell; null when the cat isn't here.</summary>
    public string? SessionOf(Guid playerId) => _leases.TryGetValue(playerId, out var lease) ? lease.Session : null;

    private static (int X, int Z) CellOf(Position position) =>
        ((int)Math.Floor(position.X / WorldSpace.ViewDistance), (int)Math.Floor(position.Z / WorldSpace.ViewDistance));

    // Keeps the grid in step with where the cat now stands.
    private void Put(CatInRoom cat)
    {
        if (_cats.TryGetValue(cat.PlayerId, out var before) && CellOf(before.Position) != CellOf(cat.Position))
        {
            LeaveCell(cat.PlayerId, before.Position);
        }

        _cats[cat.PlayerId] = cat;
        if (!_cells.TryGetValue(CellOf(cat.Position), out var cell))
        {
            _cells[CellOf(cat.Position)] = cell = [];
        }

        cell.Add(cat.PlayerId);
    }

    private void LeaveCell(Guid playerId, Position position)
    {
        var key = CellOf(position);
        if (_cells.TryGetValue(key, out var cell) && cell.Remove(playerId) && cell.Count == 0)
        {
            _cells.Remove(key);
        }
    }

    /// <summary>The cats within <see cref="WorldSpace.ViewDistance"/> of a spot, and some beyond it: those in its cell and the eight around.</summary>
    public IEnumerable<CatInRoom> CatsAround(Position position)
    {
        var (cx, cz) = CellOf(position);
        for (var x = cx - 1; x <= cx + 1; x++)
        {
            for (var z = cz - 1; z <= cz + 1; z++)
            {
                if (_cells.TryGetValue((x, z), out var cell))
                {
                    foreach (var id in cell)
                    {
                        yield return _cats[id];
                    }
                }
            }
        }
    }

    private bool Controls(Guid playerId, string session) =>
        _leases.TryGetValue(playerId, out var lease) && lease.Session == session;

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in the world.");

    private void Forget(Guid playerId)
    {
        if (_cats.Remove(playerId, out var gone))
        {
            LeaveCell(playerId, gone.Position);
        }

        _leases.Remove(playerId);
        _lastEmotes.Remove(playerId);
        _heard.Remove(playerId);
        _lines.Remove(playerId);
        _lastCasts.Remove(playerId);
        _walks.Remove(playerId);
    }
}

/// <summary>A line a player heard, and where its speaker stood.</summary>
public sealed record HeardLine(ChatLine Line, Position From);
