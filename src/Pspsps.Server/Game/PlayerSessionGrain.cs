using Orleans.Runtime;
using Pspsps.Domain;

namespace Pspsps.Server.Game;

/// <summary>
/// A player's current tab and the room its cat is in. The room is only a pointer: <see cref="Locate"/> asks that room
/// whether the cat is still there (the room's lease is the truth), and forgets a room that no longer has it. While it
/// points at a room it checks it every <see cref="CheckEvery"/>, which also keeps it from being collected while its
/// player sits idle; once the room no longer has the cat, it stops.
/// </summary>
internal sealed class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    /// <summary>How often a session that points at a room checks it.</summary>
    private static TimeSpan CheckEvery => SessionLease.Duration;

    private string? _current;
    private string? _roomKey;
    private IGrainTimer? _check;

    public Task<string?> Claim(string connectionId)
    {
        var previous = _current == connectionId ? null : _current;
        if (previous is not null)
        {
            // The new tab says where its cat is once it's in a room.
            Forget();
        }

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
            Forget();
            DeactivateOnIdle();
        }

        return Task.CompletedTask;
    }

    public Task Arrived(string connectionId, string roomKey)
    {
        _current ??= connectionId;
        if (_current != connectionId)
        {
            return Task.CompletedTask;
        }

        _roomKey = roomKey;
        _check ??= this.RegisterGrainTimer(
            static (grain, _) => grain.Locate(),
            this,
            new GrainTimerCreationOptions { DueTime = CheckEvery, Period = CheckEvery, KeepAlive = true });
        return Task.CompletedTask;
    }

    public async Task<PlayerPlace?> Locate()
    {
        if (_current is not { } session || _roomKey is not { } key)
        {
            return null;
        }

        if (await GrainFactory.GetGrain<IRoomGrain>(key).Has(this.GetPrimaryKey(), session))
        {
            var (worldId, roomId) = RoomKey.Parse(key);
            return new PlayerPlace(worldId, roomId);
        }

        // Calls run one at a time, so nothing moved the pointer while the room answered.
        Forget();
        return null;
    }

    private void Forget()
    {
        _roomKey = null;
        _check?.Dispose();
        _check = null;
    }
}
