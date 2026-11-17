namespace Pspsps.Server.Game;

internal sealed class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private string? _current;

    public Task<string?> Claim(string connectionId)
    {
        var previous = _current == connectionId ? null : _current;
        _current = connectionId;
        return Task.FromResult(previous);
    }

    public Task<bool> IsCurrent(string connectionId)
    {
        _current ??= connectionId;
        return Task.FromResult(_current == connectionId);
    }

    public Task Release(string connectionId)
    {
        if (_current == connectionId)
        {
            _current = null;
            DeactivateOnIdle();
        }

        return Task.CompletedTask;
    }
}
