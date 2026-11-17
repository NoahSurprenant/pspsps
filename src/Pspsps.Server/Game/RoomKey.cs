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
}
