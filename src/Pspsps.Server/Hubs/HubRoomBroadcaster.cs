using Microsoft.AspNetCore.SignalR;
using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>Room broadcasts through SignalR groups; the Redis backplane carries them to the other pods.</summary>
internal sealed class HubRoomBroadcaster(IHubContext<GameHub, IGameClient> hub) : IRoomBroadcaster
{
    public Task CatJoined(string roomKey, CatView cat) => hub.Clients.Group(roomKey).CatJoined(cat);

    public Task CatLeft(string roomKey, Guid playerId) => hub.Clients.Group(roomKey).CatLeft(playerId);

    public Task CatMoved(string roomKey, Guid playerId, double x, double z) =>
        hub.Clients.Group(roomKey).CatMoved(playerId, x, z);

    public Task ChatSaid(string roomKey, ChatView line) => hub.Clients.Group(roomKey).ChatSaid(line);

    public Task CatEmoted(string roomKey, EmoteView emote) => hub.Clients.Group(roomKey).CatEmoted(emote);
}
