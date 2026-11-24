using Microsoft.AspNetCore.SignalR;
using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>
/// Room broadcasts through SignalR groups, and messages to one tab through its connection; the Redis backplane carries
/// them to the pod holding each connection.
/// </summary>
internal sealed class HubRoomBroadcaster(IHubContext<GameHub, IGameClient> hub, TimeProvider time) : IRoomBroadcaster
{
    public Task CatJoined(string roomKey, CatView cat) => hub.Clients.Group(roomKey).CatJoined(cat);

    public Task CatLeft(string roomKey, Guid playerId) => hub.Clients.Group(roomKey).CatLeft(playerId);

    public Task CatMoved(string roomKey, Guid playerId, double x, double z) =>
        hub.Clients.Group(roomKey).CatMoved(playerId, x, z);

    public Task ChatSaid(string roomKey, ChatView line) => hub.Clients.Group(roomKey).ChatSaid(line);

    public Task CatEmoted(string roomKey, EmoteView emote) => hub.Clients.Group(roomKey).CatEmoted(emote);

    public Task CatDressed(string roomKey, OutfitView outfit) => hub.Clients.Group(roomKey).CatDressed(outfit);

    public Task CatRenamed(string roomKey, RenameView rename) => hub.Clients.Group(roomKey).CatRenamed(rename);

    public Task HomeRenamed(string roomKey, string ownerName) => hub.Clients.Group(roomKey).HomeRenamed(ownerName);

    public Task CatJoined(IReadOnlyList<string> sessions, CatView cat) => hub.Clients.Clients(sessions).CatJoined(cat);

    public Task CatLeft(IReadOnlyList<string> sessions, Guid playerId) => hub.Clients.Clients(sessions).CatLeft(playerId);

    public Task CatMoved(IReadOnlyList<string> sessions, Guid playerId, double x, double z) =>
        hub.Clients.Clients(sessions).CatMoved(playerId, x, z);

    public Task ChatSaid(IReadOnlyList<string> sessions, ChatView line) => hub.Clients.Clients(sessions).ChatSaid(line);

    public Task CatEmoted(IReadOnlyList<string> sessions, EmoteView emote) => hub.Clients.Clients(sessions).CatEmoted(emote);

    public Task CatDressed(IReadOnlyList<string> sessions, OutfitView outfit) => hub.Clients.Clients(sessions).CatDressed(outfit);

    public Task CatRenamed(IReadOnlyList<string> sessions, RenameView rename) => hub.Clients.Clients(sessions).CatRenamed(rename);

    public Task FishBit(string session) => hub.Clients.Client(session).FishBit();

    public Task FishGotAway(string session) => hub.Clients.Client(session).FishGotAway();

    public Task RoomLost(string session) => hub.Clients.Client(session).RoomLost();

    public Task HomeChanged(string roomKey, HomeView home) => hub.Clients.Group(roomKey).HomeChanged(home);

    public async Task SentOut(string roomKey, string session, string message)
    {
        // Called by the room's grain, on any pod: bounded, so a connection that closed (or whose pod died) doesn't hold up
        // the room for the backplane's 30 seconds.
        await HubGroups.RemoveAsync(hub, session, roomKey, time);
        // Which home: a tab that has gone into another meanwhile stays there.
        await hub.Clients.Client(session).SentOut(RoomKey.HomeOwner(roomKey) ?? Guid.Empty, message);
    }
}
