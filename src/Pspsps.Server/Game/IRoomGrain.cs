namespace Pspsps.Server.Game;

/// <summary>One room of one world, keyed by <see cref="RoomKey"/>. One activation in the whole cluster.</summary>
public interface IRoomGrain : IGrainWithStringKey
{
    /// <summary>Puts the player's cat in the room, controlled by <paramref name="session"/> (the tab's connection).</summary>
    Task<RoomSnapshot> Join(PlayerCard player, string session);

    /// <summary>Takes the cat out, unless another session (a newer tab) controls it now.</summary>
    Task Leave(Guid playerId, string session);

    Task Move(Guid playerId, double x, double z);

    Task Say(Guid playerId, string text);

    Task Emote(Guid playerId, string emote);
}
