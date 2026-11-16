using Pspsps.Domain.Worlds;

namespace Pspsps.Server.Game;

// Kept alive: its counts exist only in memory.
[KeepAlive]
internal sealed class WorldGrain : Grain, IWorldGrain
{
    // Connections per player: a player with two tabs open counts once against the cap.
    private readonly Dictionary<Guid, int> _connections = [];
    private WorldDefinition _world = null!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var id = this.GetPrimaryKeyString();
        _world = WorldCatalog.Find(id) ?? throw new InvalidOperationException($"No world '{id}'.");
        return Task.CompletedTask;
    }

    public Task<bool> TryEnter(Guid playerId)
    {
        if (_connections.TryGetValue(playerId, out var count))
        {
            _connections[playerId] = count + 1;
            return Task.FromResult(true);
        }

        if (_connections.Count >= _world.Capacity)
        {
            return Task.FromResult(false);
        }

        _connections[playerId] = 1;
        return Task.FromResult(true);
    }

    public Task Exit(Guid playerId)
    {
        if (_connections.TryGetValue(playerId, out var count))
        {
            if (count > 1)
            {
                _connections[playerId] = count - 1;
            }
            else
            {
                _connections.Remove(playerId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> GetPopulation() => Task.FromResult(_connections.Count);
}
