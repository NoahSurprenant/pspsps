namespace Pspsps.Server.Game;

/// <summary>One room of one world, keyed by <see cref="RoomKey"/>. One activation in the whole cluster.</summary>
public interface IRoomGrain : IGrainWithStringKey
{
    Task<RoomSnapshot> Join(PlayerCard player);

    Task Leave(Guid playerId);

    Task Move(Guid playerId, double x, double z);

    Task Say(Guid playerId, string text);
}
