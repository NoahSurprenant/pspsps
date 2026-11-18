using System.Collections.Concurrent;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;

namespace Pspsps.Server.Moderation;

/// <summary>
/// Whether a player is banned, for the hub to refuse their every call: read from the database at most once every
/// <see cref="KeepFor"/> per player on this pod (a move is a call), and again at once after a change made on this pod.
/// A ban decided on another pod is seen here within <see cref="KeepFor"/>; a ban read here ends at its time.
/// </summary>
public sealed class StandingCache(
    IServiceScopeFactory scopes, [FromKeyedServices(ModerationClock.Key)] TimeProvider time)
{
    /// <summary>How long a player's standing, once read, is trusted on this pod.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(5);

    // Past this many players kept, the stale ones are dropped at the next read.
    private const int PruneAbove = 1000;

    private readonly ConcurrentDictionary<Guid, Read> _read = new();

    // Bumped by every Forget: a read that started before one isn't kept (it may predate the change).
    private long _forgets;

    /// <summary>The ban the player is under now, if any.</summary>
    public async Task<Sanction?> BanOfAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (!_read.TryGetValue(playerId, out var read) || now - read.At >= KeepFor || now < read.At)
        {
            var forgets = Interlocked.Read(ref _forgets);
            await using var scope = scopes.CreateAsyncScope();
            var standing = await scope.ServiceProvider.GetRequiredService<IModerationService>()
                .StandingAsync(playerId, cancellationToken);
            read = new Read(standing, now);
            if (_read.Count > PruneAbove)
            {
                Prune(now);
            }

            if (Interlocked.Read(ref _forgets) == forgets)
            {
                _read[playerId] = read;
            }
        }

        return read.Standing.Ban is { } ban && ban.InForceAt(time.GetUtcNow()) ? ban : null;
    }

    /// <summary>Forgets what was read of the player: their standing changed.</summary>
    public void Forget(Guid playerId)
    {
        Interlocked.Increment(ref _forgets);
        _read.TryRemove(playerId, out _);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (playerId, read) in _read)
        {
            if (now - read.At >= KeepFor)
            {
                _read.TryRemove(playerId, out _);
            }
        }
    }

    private sealed record Read(Standing Standing, DateTimeOffset At);
}
