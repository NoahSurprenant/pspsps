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
            // Interleaves like Locate, so the player's own calls never wait behind it.
            new GrainTimerCreationOptions
            {
                DueTime = CheckEvery, Period = CheckEvery, KeepAlive = true, Interleave = true,
            });
        return Task.CompletedTask;
    }

    public async Task<PlayerPlace?> Locate()
    {
        if (_current is not { } session)
        {
            // Asked after a player who isn't playing (a friends list asks after every friend): nothing to keep.
            DeactivateOnIdle();
            return null;
        }

        if (_roomKey is not { } key)
        {
            return null;
        }

        // Out in the open world the room is the one whose floor the cat stands on, not the world's one grain.
        var room = GrainFactory.GetGrain<IRoomGrain>(key);
        var here = await room.WhereIs(this.GetPrimaryKey(), session);
        if (here is not null)
        {
            return new PlayerPlace(RoomKey.Parse(key).WorldId, here);
        }

        // Other calls ran while the room answered (Locate interleaves): forget the room only if nothing moved the pointer.
        if (_current == session && _roomKey == key)
        {
            Forget();
        }

        return null;
    }

    private void Forget()
    {
        _roomKey = null;
        _check?.Dispose();
        _check = null;
    }
}
