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

    private readonly Dictionary<Guid, CatInRoom> _cats = [];

    // Which session (a browser tab's connection) controls each cat, and when it last renewed its lease: a player is one
    // cat, in their newest tab.
    private readonly Dictionary<Guid, Lease> _leases = [];
    private readonly Queue<ChatLine> _chat = new();

    public RoomDefinition Room { get; } = room;

    public IReadOnlyCollection<CatInRoom> Cats => _cats.Values;

    public IReadOnlyCollection<ChatLine> RecentChat => _chat;

    public bool IsFull => _cats.Count >= Room.Capacity;

    /// <summary>
    /// Puts a cat at the room's spawn point, controlled by <paramref name="session"/>, whose lease starts
    /// <paramref name="now"/>. A cat already here (the player's other tab) keeps its spot and passes to the new session.
    /// </summary>
    public CatInRoom Join(Guid playerId, string displayName, FurColor furColor, string session, DateTimeOffset now)
    {
        if (_cats.TryGetValue(playerId, out var existing))
        {
            _leases[playerId] = new Lease(session, now);
            return existing;
        }

        if (IsFull)
        {
            throw new DomainException($"{Room.Name} is full.");
        }

        var cat = new CatInRoom(playerId, displayName, furColor, Room.Spawn);
        _cats.Add(playerId, cat);
        _leases[playerId] = new Lease(session, now);
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
        return _cats.Remove(playerId);
    }

    /// <summary>
    /// Renews the lease of the cat <paramref name="session"/> controls; false when the cat isn't here or another session
    /// controls it. A renewal never brings a cat back.
    /// </summary>
    public bool Renew(Guid playerId, string session, DateTimeOffset now)
    {
        if (!Controls(playerId, session))
        {
            return false;
        }

        _leases[playerId] = new Lease(session, now);
        return true;
    }

    /// <summary>
    /// Takes out every cat whose session stopped renewing its lease (its connection went away without saying so, as when
    /// its pod dies). Returns whose cats left.
    /// </summary>
    public IReadOnlyList<Guid> RemoveExpired(DateTimeOffset now)
    {
        var expired = _leases.Where(l => SessionLease.HasExpired(l.Value.RenewedAt, now)).Select(l => l.Key).ToList();
        foreach (var playerId in expired)
        {
            _leases.Remove(playerId);
            _cats.Remove(playerId);
        }

        return expired;
    }

    /// <summary>Walks the cat towards <paramref name="target"/>, stopping at the room's walls. Returns where it ends up.</summary>
    public Position Move(Guid playerId, Position target)
    {
        var cat = Get(playerId);
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Z))
        {
            throw new DomainException("That isn't a spot in the room.");
        }

        var position = Room.Clamp(target);
        _cats[playerId] = cat with { Position = position };
        return position;
    }

    public ChatLine Say(Guid playerId, string text, DateTimeOffset now)
    {
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

        var line = new ChatLine(playerId, trimmed, now);
        _chat.Enqueue(line);
        while (_chat.Count > ChatHistory)
        {
            _chat.Dequeue();
        }

        return line;
    }

    private bool Controls(Guid playerId, string session) =>
        _leases.TryGetValue(playerId, out var lease) && lease.Session == session;

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in this room.");

    private readonly record struct Lease(string Session, DateTimeOffset RenewedAt);
}
