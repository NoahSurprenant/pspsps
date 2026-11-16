namespace Pspsps.Server.Game;

/// <summary>A room grain's key and SignalR group name: one room of one world, "world/room".</summary>
public static class RoomKey
{
    public static string For(string worldId, string roomId) => $"{worldId}/{roomId}";

    public static (string WorldId, string RoomId) Parse(string key)
    {
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return (key[..slash], key[(slash + 1)..]);
    }
}
