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
    private readonly Dictionary<Guid, SessionLease> _leases = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastEmotes = [];
    private readonly Dictionary<Guid, Queue<HeardLine>> _heard = [];

    public WorldSpace Space { get; } = space;

    /// <summary>Cats out in the open at once, over every outdoor room.</summary>
    public int Capacity { get; } = capacity;

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
        _cats.Add(playerId, cat);
        _leases[playerId] = SessionLease.Start(session, now);
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

    /// <summary>Walks the cat towards <paramref name="target"/>, a world spot, stopping at the edge of the world's floor.</summary>
    public Position Move(Guid playerId, Position target)
    {
        var cat = Get(playerId);
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Z))
        {
            throw new DomainException("That isn't a spot in the world.");
        }

        var position = Space.Clamp(target);
        _cats[playerId] = cat with { Position = position, Pose = CatPose.Standing };
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
    /// within <see cref="WorldSpace.ViewDistance"/> of the speaker, the speaker too. Each listener keeps it in what they
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
        var heard = _cats.Values
            .Where(cat => cat.PlayerId == playerId || cat.Position.DistanceTo(speaker.Position) <= WorldSpace.ViewDistance)
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
        return [.. _cats.Values
            .Where(cat => cat.PlayerId != playerId)
            .Select(cat => (Cat: cat, Distance: cat.Position.DistanceTo(me.Position)))
            .Where(x => x.Distance <= WorldSpace.ViewDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Cat.PlayerId)
            .Take(WorldSpace.MaxVisibleCats)
            .Select(x => x.Cat)];
    }

    public bool Has(Guid playerId, string session) => _cats.ContainsKey(playerId) && Controls(playerId, session);

    /// <summary>The cat, or null when it isn't here.</summary>
    public CatInRoom? Find(Guid playerId) => _cats.GetValueOrDefault(playerId);

    /// <summary>The session controlling the cat, to tell; null when the cat isn't here.</summary>
    public string? SessionOf(Guid playerId) => _leases.TryGetValue(playerId, out var lease) ? lease.Session : null;

    private bool Controls(Guid playerId, string session) =>
        _leases.TryGetValue(playerId, out var lease) && lease.Session == session;

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in the world.");

    private void Forget(Guid playerId)
    {
        _cats.Remove(playerId);
        _leases.Remove(playerId);
        _lastEmotes.Remove(playerId);
        _heard.Remove(playerId);
    }
}

/// <summary>A line a player heard, and where its speaker stood.</summary>
public sealed record HeardLine(ChatLine Line, Position From);
