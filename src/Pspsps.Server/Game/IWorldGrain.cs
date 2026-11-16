namespace Pspsps.Server.Game;

/// <summary>One world, keyed by its ID: who is in it, against its population cap.</summary>
public interface IWorldGrain : IGrainWithStringKey
{
    /// <summary>Counts one more connection of the player; false when the world is full and the player isn't in it yet.</summary>
    Task<bool> TryEnter(Guid playerId);

    Task Leave(Guid playerId);

    Task<int> GetPopulation();
}
