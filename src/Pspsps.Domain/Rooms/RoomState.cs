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
    private readonly Queue<ChatLine> _chat = new();

    public RoomDefinition Room { get; } = room;

    public IReadOnlyCollection<CatInRoom> Cats => _cats.Values;

    public IReadOnlyCollection<ChatLine> RecentChat => _chat;

    public bool IsFull => _cats.Count >= Room.Capacity;

    /// <summary>Puts a cat at the room's spawn point. A cat already here (a second tab) keeps its spot.</summary>
    public CatInRoom Join(Guid playerId, string displayName, FurColor furColor)
    {
        if (_cats.TryGetValue(playerId, out var existing))
        {
            return existing;
        }

        if (IsFull)
        {
            throw new DomainException($"{Room.Name} is full.");
        }

        var cat = new CatInRoom(playerId, displayName, furColor, Room.Spawn);
        _cats.Add(playerId, cat);
        return cat;
    }

    /// <summary>Removes the cat; false when it wasn't here.</summary>
    public bool Leave(Guid playerId) => _cats.Remove(playerId);

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

    private CatInRoom Get(Guid playerId) =>
        _cats.TryGetValue(playerId, out var cat) ? cat : throw new DomainException("You aren't in this room.");
}
