using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>
/// A room grain's key and SignalR group name: one room of one world, "world/room"; or a player's home, which is in no
/// world (cats from every world visit it), "homes/owner-id".
/// </summary>
public static class RoomKey
{
    /// <summary>What a home's key starts with, in place of a world: no world is called this.</summary>
    public const string Homes = "homes";

    public static string For(string worldId, string roomId) => $"{worldId}/{roomId}";

    /// <summary>The room ID of a world's open world: every outdoor room, as one room grain.</summary>
    public const string Overworld = "overworld";

    /// <summary>The key of the open world of <paramref name="worldId"/>.</summary>
    public static string ForOverworld(string worldId) => For(worldId, Overworld);

    /// <summary>The key of the grain that holds a cat standing in the room <paramref name="roomId"/>: the open world's when the room is on the world map (<paramref name="placed"/>).</summary>
    public static string Grain(string worldId, string roomId, bool placed) => placed ? ForOverworld(worldId) : For(worldId, roomId);

    /// <summary>Whether the key is a world's open world.</summary>
    public static bool IsOverworld(string key) => Parse(key).RoomId == Overworld;

    /// <summary>What a cat joining the open world passes in place of a door: the room it comes in at, and the door of it if any ("room" or "room/door").</summary>
    public static string Arrival(string roomId, string? doorId) => doorId is null ? roomId : $"{roomId}/{doorId}";

    public static (string RoomId, string? DoorId) ParseArrival(string? arrival)
    {
        ArgumentException.ThrowIfNullOrEmpty(arrival);
        var slash = arrival.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? (arrival, null) : (arrival[..slash], arrival[(slash + 1)..]);
    }

    /// <summary>The key of <paramref name="ownerId"/>'s home.</summary>
    public static string ForHome(Guid ownerId) => For(Homes, ownerId.ToString());

    public static (string WorldId, string RoomId) Parse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return (key[..slash], key[(slash + 1)..]);
    }

    /// <summary>Whose home the key is, or null when it's a world's room.</summary>
    public static Guid? HomeOwner(string key)
    {
        var (worldId, roomId) = Parse(key);
        return HomeOwner(worldId, roomId);
    }

    /// <summary>Whose home the room <paramref name="roomId"/> of <paramref name="worldId"/> (a key's parts) is, or null.</summary>
    public static Guid? HomeOwner(string worldId, string roomId) =>
        worldId == Homes && Guid.TryParse(roomId, out var owner) ? owner : null;

    /// <summary>The key of <paramref name="place"/>, in the world <paramref name="worldId"/> (a home's key has none).</summary>
    public static string For(string worldId, Whereabouts place)
    {
        ArgumentNullException.ThrowIfNull(place);
        return place.HomeOwner is { } owner ? ForHome(owner) : For(worldId, place.RoomId!);
    }

    /// <summary>Which place in its world the room <paramref name="key"/> is: one of the world's rooms, or a home.</summary>
    public static Whereabouts WhereaboutsOf(string key) =>
        HomeOwner(key) is { } owner ? Whereabouts.Home(owner) : Whereabouts.Room(Parse(key).RoomId);
}
