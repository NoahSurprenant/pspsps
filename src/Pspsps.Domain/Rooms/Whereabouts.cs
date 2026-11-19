namespace Pspsps.Domain.Rooms;

/// <summary>
/// A place a cat can be in, in the world it is in: one of the world's rooms (<see cref="RoomId"/>), or a player's home
/// (<see cref="HomeOwner"/>), which is in no world's catalogue and is visited from one.
/// </summary>
public sealed record Whereabouts
{
    private Whereabouts(string? roomId, Guid? homeOwner)
    {
        RoomId = roomId;
        HomeOwner = homeOwner;
    }

    /// <summary>The world's room, when this is one; null for a home.</summary>
    public string? RoomId { get; }

    /// <summary>Whose home this is, when it is one; null for a world's room.</summary>
    public Guid? HomeOwner { get; }

    public static Whereabouts Room(string roomId) => new(roomId ?? throw new ArgumentNullException(nameof(roomId)), null);

    public static Whereabouts Home(Guid ownerId) => new(null, ownerId);
}
