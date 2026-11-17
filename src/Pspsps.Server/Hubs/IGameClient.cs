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

    /// <summary>
    /// The world no longer counts this tab and is full (it came back empty after its pod died, and filled up before this
    /// tab's renewal arrived): pick a world again. <paramref name="message"/> says why.
    /// </summary>
    Task WorldLost(string message);

    Task CatEmoted(EmoteView emote);

    /// <summary>Something bit this tab's line: reel in within <see cref="Pspsps.Domain.Fishing.FishingLine.BiteWindow"/>.</summary>
    Task FishBit();

    /// <summary>The bite on this tab's line wasn't reeled in time: it got away, and the line is out of the water.</summary>
    Task FishGotAway();

    /// <summary>A cat in the room caught something.</summary>
    Task CatFished(CatchView caught);

    /// <summary>The player opened the game in another tab, which is the one playing now.</summary>
    Task SessionReplaced();
}
