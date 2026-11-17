using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>What the server calls on browsers (client/src/net.ts handles each).</summary>
public interface IGameClient
{
    Task CatJoined(CatView cat);

    Task CatLeft(Guid playerId);

    Task CatMoved(Guid playerId, double x, double z);

    Task ChatSaid(ChatView line);

    /// <summary>
    /// The room no longer has this tab's cat (its lease ran out, or the room came back empty after its pod died): enter the
    /// world again, then go back to that room.
    /// </summary>
    Task RoomLost();

    /// <summary>The player opened the game in another tab, which is the one playing now.</summary>
    Task SessionReplaced();
}
