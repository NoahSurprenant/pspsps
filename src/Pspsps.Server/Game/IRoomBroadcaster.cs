namespace Pspsps.Server.Game;

/// <summary>Sends a room's changes to every browser in it, on whichever pod each is connected to.</summary>
public interface IRoomBroadcaster
{
    Task CatJoined(string roomKey, CatView cat);

    Task CatLeft(string roomKey, Guid playerId);

    Task CatMoved(string roomKey, Guid playerId, double x, double z);

    Task ChatSaid(string roomKey, ChatView line);

    Task CatEmoted(string roomKey, EmoteView emote);

    Task CatDressed(string roomKey, OutfitView outfit);

    /// <summary>Tells one tab (its connection) that its line has a bite: reel now.</summary>
    Task FishBit(string session);

    /// <summary>Tells one tab that the bite on its line wasn't reeled in time: the fish got away, the line is in.</summary>
    Task FishGotAway(string session);
}
