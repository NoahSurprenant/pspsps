using Pspsps.Domain.Fishing;
using Pspsps.Domain.Players;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// Who is in one room of one world and what they said lately. Lives in memory only (the room's actor holds it) and is
/// never saved: a room that empties forgets everything.
/// </summary>
public sealed class RoomState(RoomDefinition room)
{
    public const int MaxChatLength = 120;
    public const int ChatHistory = 20;

    /// <summary>How long a cat waits between emotes.</summary>
    public static TimeSpan EmoteCooldown { get; } = TimeSpan.FromSeconds(1);

    private readonly Dictionary<Guid, CatInRoom> _cats = [];

    // Which session (a browser tab's connection) controls each cat, and when it last renewed its lease: a player is one
    // cat, in their newest tab.
    private readonly Dictionary<Guid, SessionLease> _leases = [];
    private readonly Queue<ChatLine> _chat = new();

    // When each cat last emoted, for the cooldown.
    private readonly Dictionary<Guid, DateTimeOffset> _lastEmotes = [];

    // Lines in the pond, by cat: cast, waiting for the bite only the server knows.
    private readonly Dictionary<Guid, FishingLine> _lines = [];

    public RoomDefinition Room { get; } = room;

    public IReadOnlyCollection<CatInRoom> Cats => _cats.Values;

    public IReadOnlyCollection<ChatLine> RecentChat => _chat;

    public bool IsFull => _cats.Count >= Room.Capacity;

    /// <summary>
    /// Puts a cat at the room's spawn point, controlled by <paramref name="session"/>, whose lease starts
    /// <paramref name="now"/>. A cat already here (the player's other tab) keeps its spot and passes to the new session;
    /// a line the other tab cast is reeled in, with nothing on it (the new tab never cast it).
    /// </summary>
    public CatInRoom Join(Guid playerId, string displayName, FurColor furColor, string session, DateTimeOffset now)
    {
        if (_cats.TryGetValue(playerId, out var existing))
        {
            if (!Controls(playerId, session))
            {
                _lines.Remove(playerId);
            }

            _leases[playerId] = SessionLease.Start(session, now);
            return existing;
        }

        if (IsFull)
        {
            throw new DomainException($"{Room.Name} is full.");
        }

        var cat = new CatInRoom(playerId, displayName, furColor, Room.Spawn);
        _cats.Add(playerId, cat);
        _leases[playerId] = SessionLease.Start(session, now);
        return cat;
    }

    /// <summary>
    /// Removes the cat when <paramref name="session"/> controls it; false when it wasn't here or another session took it
    /// over (a replaced tab closing must not take the newer tab's cat with it).
    /// </summary>
    public bool Leave(Guid playerId, string session)
    {
        if (!Controls(playerId, session))
        {
            return false;
        }

        _leases.Remove(playerId);
        _lastEmotes.Remove(playerId);
        _lines.Remove(playerId);
        return _cats.Remove(playerId);
    }

    /// <summary>
    /// Renews the lease of the cat <paramref name="session"/> controls. A renewal never brings a cat back: when the cat is
    /// gone, the session has lost it (and its tab should walk back in); when another session took it over, it hasn't.
    /// </summary>
    public CatRenewal Renew(Guid playerId, string session, DateTimeOffset now)
    {
        if (Controls(playerId, session))
        {
            _leases[playerId] = _leases[playerId].Renew(now);
            return CatRenewal.Renewed;
        }

        return _cats.ContainsKey(playerId) ? CatRenewal.TakenOver : CatRenewal.Gone;
    }

    /// <summary>
    /// Takes out every cat whose session stopped renewing its lease (its connection went away without saying so, as when
    /// its pod dies). Returns whose cats left.
    /// </summary>
    public IReadOnlyList<Guid> RemoveExpired(DateTimeOffset now)
    {
        var expired = _leases.Where(l => l.Value.HasExpired(now)).Select(l => l.Key).ToList();
        foreach (var playerId in expired)
        {
            _leases.Remove(playerId);
            _cats.Remove(playerId);
            _lastEmotes.Remove(playerId);
            _lines.Remove(playerId);
        }

        return expired;
    }

    /// <summary>
    /// Walks the cat towards <paramref name="target"/>, stopping at the room's walls. Returns where it ends up. A cat that
    /// walks off reels its line in, with nothing on it.
    /// </summary>
    public Position Move(Guid playerId, Position target)
    {
        var cat = Get(playerId);
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Z))
        {
            throw new DomainException("That isn't a spot in the room.");
        }

        var position = Room.Clamp(target);
        _cats[playerId] = cat with { Position = position, Pose = CatPose.Standing };
        _lines.Remove(playerId);
        return position;
    }

    /// <summary>The cat's line in the water, if any: a bite applies only to the line it was cast for.</summary>
    public FishingLine? LineOf(Guid playerId) => _lines.GetValueOrDefault(playerId);

    /// <summary>
    /// Casts the cat's line into the room's pond: it must stand at the water's edge, with no line already out.
    /// <paramref name="roll"/> in [0, 1) picks when the fish bites.
    /// </summary>
    public FishingLine Cast(Guid playerId, DateTimeOffset now, double roll)
    {
        var cat = Get(playerId);
        var pond = Room.Pond ?? throw new DomainException("There's no water to fish in here.");
        if (!pond.InReach(cat.Position))
        {
            throw new DomainException("Walk up to the pond to fish.");
        }

        if (_lines.ContainsKey(playerId))
        {
            throw new DomainException("Your line is already in the water.");
        }

        var line = FishingLine.Cast(now, roll);
        _lines[playerId] = line;
        return line;
    }

    /// <summary>
    /// The fish bites <paramref name="line"/> at <paramref name="now"/>, if it is still the cat's line (it may have been
    /// reeled in, walked off or taken over since). Returns the session to tell (the one controlling the cat), or null
    /// when there's nobody to tell.
    /// </summary>
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

    /// <summary>
    /// Reels the cat's line in: a fish if it's within the bite window (from when the cat was told), nothing if too early
    /// or too late. Either way the line is out of the water after. <paramref name="roll"/> in [0, 1) picks the fish.
    /// </summary>
    public ReelResult Reel(Guid playerId, DateTimeOffset now, double roll)
    {
        Get(playerId);
        if (!_lines.Remove(playerId, out var line))
        {
            throw new DomainException("Cast your line first.");
        }

        return line.Reel(now, roll);
    }

    /// <summary>
    /// Says a line in the room, through <paramref name="filter"/>: words it masks are starred out, and a word it refuses
    /// keeps the whole line out of the room.
    /// </summary>
    public ChatLine Say(Guid playerId, string text, ChatFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Get(playerId);
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("Say something.");
        }

        if (trimmed.Length > MaxChatLength)
        {
            throw new DomainException($"Chat messages are at most {MaxChatLength} characters.");
        }

        var line = new ChatLine(playerId, filter.Apply(trimmed), now);
        _chat.Enqueue(line);
        while (_chat.Count > ChatHistory)
        {
            _chat.Dequeue();
        }

        return line;
    }

    /// <summary>
    /// The cat does <paramref name="emote"/>, one of <see cref="Emotes.All"/>, at most once per <see cref="EmoteCooldown"/>.
    /// Sit and sleep are held until the cat walks; meow and wave leave the pose as it is.
    /// </summary>
    public CatEmote Emote(Guid playerId, string emote, DateTimeOffset now)
    {
        var cat = Get(playerId);
        if (!Emotes.IsKnown(emote))
        {
            throw new DomainException("That isn't an emote.");
        }

        if (_lastEmotes.TryGetValue(playerId, out var last) && now - last < EmoteCooldown)
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

    private bool Controls(Guid playerId, string session) =>
        _leases.TryGetValue(playerId, out var lease) && lease.Session == session;

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in this room.");
}
