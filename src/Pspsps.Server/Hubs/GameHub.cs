using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pspsps.Application.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Worlds;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>
/// The browser's one realtime connection. Thin: it finds the player and forwards to the world and room grains, which
/// may live on any pod. Each connection is in at most one world and one room.
/// </summary>
[Authorize]
public sealed class GameHub(IGrainFactory grains, IPlayerService players) : Hub<IGameClient>
{
    private const string WorldItem = "world";
    private const string RoomItem = "room";

    public async Task<IReadOnlyList<WorldSummary>> GetWorlds()
    {
        var summaries = new List<WorldSummary>();
        foreach (var world in WorldCatalog.All)
        {
            var population = await grains.GetGrain<IWorldGrain>(world.Id).GetPopulation();
            summaries.Add(new WorldSummary(world.Id, world.Name, population, world.Capacity));
        }

        return summaries;
    }

    public async Task<RoomSnapshot> EnterWorld(string worldId)
    {
        var world = WorldCatalog.Find(worldId) ?? throw new HubException("That world doesn't exist.");
        await LeaveWorld();
        if (!await grains.GetGrain<IWorldGrain>(world.Id).TryEnter(PlayerId))
        {
            throw new HubException($"{world.Name} is full. Try another world.");
        }

        Context.Items[WorldItem] = world.Id;
        return await JoinRoom(world.Id, RoomCatalog.Default.Id);
    }

    public async Task<RoomSnapshot> GoToRoom(string roomId)
    {
        var worldId = Context.Items[WorldItem] as string ?? throw new HubException("Pick a world first.");
        var room = RoomCatalog.Find(roomId) ?? throw new HubException("That room doesn't exist.");
        await LeaveRoom();
        return await JoinRoom(worldId, room.Id);
    }

    public Task Move(double x, double z) => InRoom(room => room.Move(PlayerId, x, z));

    public Task Say(string text) => InRoom(room => room.Say(PlayerId, text));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveWorld();
        await base.OnDisconnectedAsync(exception);
    }

    private Guid PlayerId => Context.User?.GetPlayerId() ?? throw new HubException("Sign in first.");

    private async Task<RoomSnapshot> JoinRoom(string worldId, string roomId)
    {
        var profile = await players.FindAsync(PlayerId, Context.ConnectionAborted)
            ?? throw new HubException("Your cat wasn't found. Sign in again.");
        var key = RoomKey.For(worldId, roomId);

        // In the group before joining, so nothing said in between is missed; the browser treats repeats as updates.
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        try
        {
            var card = new PlayerCard(profile.Id, profile.DisplayName, profile.FurColor.ToString());
            var snapshot = await grains.GetGrain<IRoomGrain>(key).Join(card);
            Context.Items[RoomItem] = key;
            return snapshot;
        }
        catch (GameRuleException e)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
            throw new HubException(e.Message);
        }
    }

    private async Task LeaveRoom()
    {
        if (Context.Items[RoomItem] is string key)
        {
            Context.Items.Remove(RoomItem);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
            await grains.GetGrain<IRoomGrain>(key).Leave(PlayerId);
        }
    }

    private async Task LeaveWorld()
    {
        await LeaveRoom();
        if (Context.Items[WorldItem] is string worldId)
        {
            Context.Items.Remove(WorldItem);
            await grains.GetGrain<IWorldGrain>(worldId).Leave(PlayerId);
        }
    }

    private async Task InRoom(Func<IRoomGrain, Task> action)
    {
        var key = Context.Items[RoomItem] as string ?? throw new HubException("You aren't in a room.");
        try
        {
            await action(grains.GetGrain<IRoomGrain>(key));
        }
        catch (GameRuleException e)
        {
            throw new HubException(e.Message);
        }
    }
}
