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

    Task CatRenamed(string roomKey, RenameView rename);

    /// <summary>The room is a home, and its owner (not necessarily in it) was renamed: shown as "<c>OwnerName</c>'s home".</summary>
    Task HomeRenamed(string roomKey, string ownerName);

    /// <summary>Tells one tab (its connection) that its line has a bite: reel now.</summary>
    Task FishBit(string session);

    /// <summary>Tells one tab that the bite on its line wasn't reeled in time: the fish got away, the line is in.</summary>
    Task FishGotAway(string session);

    /// <summary>
    /// Tells one tab its room no longer has its cat (the room's pod is shutting down, and the room starts over empty
    /// elsewhere): the tab walks back in at once, rather than at its next lease renewal.
    /// </summary>
    Task RoomLost(string session);

    /// <summary>Shows a home's room its furniture and who may visit, as its owner just changed them.</summary>
    Task HomeChanged(string roomKey, HomeView home);

    /// <summary>
    /// Tells one tab (its connection) its cat was sent out of the room <paramref name="roomKey"/>, which it hears nothing
    /// more from, and why.
    /// </summary>
    Task SentOut(string roomKey, string session, string message);

    // The open world tells each tab only what it can see: these go to the given tabs (connections), not a room's whole group.

    Task CatJoined(IReadOnlyList<string> sessions, CatView cat);

    Task CatLeft(IReadOnlyList<string> sessions, Guid playerId);

    Task CatMoved(IReadOnlyList<string> sessions, Guid playerId, double x, double z);

    Task ChatSaid(IReadOnlyList<string> sessions, ChatView line);

    Task CatEmoted(IReadOnlyList<string> sessions, EmoteView emote);

    Task CatDressed(IReadOnlyList<string> sessions, OutfitView outfit);

    Task CatRenamed(IReadOnlyList<string> sessions, RenameView rename);

    /// <summary>The pitch's ball was kicked or is rolling: where it is and how fast it goes.</summary>
    Task BallMoved(IReadOnlyList<string> sessions, BallView ball);
}
