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

    // Which session (a browser tab's connection) controls each cat: a player is one cat, in their newest tab.
    private readonly Dictionary<Guid, string> _sessions = [];
    private readonly Queue<ChatLine> _chat = new();

    public RoomDefinition Room { get; } = room;

    public IReadOnlyCollection<CatInRoom> Cats => _cats.Values;

    public IReadOnlyCollection<ChatLine> RecentChat => _chat;

    public bool IsFull => _cats.Count >= Room.Capacity;

    /// <summary>
    /// Puts a cat at the room's spawn point, controlled by <paramref name="session"/>. A cat already here (the player's
    /// other tab) keeps its spot and passes to the new session.
    /// </summary>
    public CatInRoom Join(Guid playerId, string displayName, FurColor furColor, string session)
    {
        if (_cats.TryGetValue(playerId, out var existing))
        {
            _sessions[playerId] = session;
            return existing;
        }

        if (IsFull)
        {
            throw new DomainException($"{Room.Name} is full.");
        }

        var cat = new CatInRoom(playerId, displayName, furColor, Room.Spawn);
        _cats.Add(playerId, cat);
        _sessions[playerId] = session;
        return cat;
    }

    /// <summary>
    /// Removes the cat when <paramref name="session"/> controls it; false when it wasn't here or another session took it
    /// over (a replaced tab closing must not take the newer tab's cat with it).
    /// </summary>
    public bool Leave(Guid playerId, string session)
    {
        if (!_sessions.TryGetValue(playerId, out var current) || current != session)
        {
            return false;
        }

        _sessions.Remove(playerId);
        return _cats.Remove(playerId);
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

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in this room.");
}
